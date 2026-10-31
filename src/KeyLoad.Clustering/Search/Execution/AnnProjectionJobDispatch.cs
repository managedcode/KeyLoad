using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal sealed class AnnProjectionJobDispatch(GrainRequestCodec codec, IServiceProvider services,
    TimeProvider clock, Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing)
{
    private const string InvalidResult = "The execution ANN maintenance result is invalid.";

    internal async Task<AnnMaintenanceResult> ExecuteAsync(global::Orleans.IGrainFactory grains,
        PrincipalRecord principal, AnnMaintenanceRequest original, DateTimeOffset expiry, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var connectionId = ConnectionExecutionIdentity.Resolve(services);
        var failures = new List<Exception>();
        AnnMaintenanceResult? result = null;
        try
        {
            if (binding is not IRequestWorkBinding execution
                || !ReferenceEquals(binding.ResultRetention, execution.OriginalProducer.ResultRetention))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
            }
            var context = codec.LifetimeReadContext;
            if (expiry <= context.Now)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }
            execution.OriginalProducer.AdmitPublication();
            var envelope = codec.Issue(GrainRequestEnvelopeConstruction.Command(requestId, codec.Incarnation,
                principal.Id, OperationKind.MaintainAnnIndex, original.CommandId, GrainPayload.Strict(original),
                expiry < context.Now + context.Lifetime ? expiry : context.Now + context.Lifetime));
            using var identity = new GrainRequestIdentityScope(services, principal, requestId,
                original.CommandId, token, connectionId: connectionId);
            try
            {
                result = await ReadAsync(grains, envelope, requestId, original, connectionId, token).ConfigureAwait(true);
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        AnnProjectionJobFailures.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
    }

    private async Task<AnnMaintenanceResult> ReadAsync(global::Orleans.IGrainFactory grains, GrainRequestEnvelope envelope,
        Guid requestId, AnnMaintenanceRequest original, Guid connectionId, CancellationToken token)
    {
        var actor = grains.GetGrain<IConnectionGrain>(connectionId);
        var purpose = new CqrsStreamPurpose(() => codec.Decode(envelope));
        var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            streamToken => actor.ExecuteStreamAsync(envelope, streamToken), serializer, requestId,
            clock, routing, purpose, token).ConfigureAwait(true);
        return Read(reply, original);
    }

    private static AnnMaintenanceResult Read(GrainOperationReply reply, AnnMaintenanceRequest original)
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? InvalidResult);
        }
        var value = GrainReplyFactory.RequireValue(reply).Value;
        if (value is not AnnMaintenanceResult result || result.CommandId != original.CommandId
            || result.Consumer != original.Consumer || result.IndexGeneration != original.IndexGeneration
            || result.Phase != AnnMaintenancePhase.Completed)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
        }
        return result;
    }
}
