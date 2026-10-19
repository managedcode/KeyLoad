using KeyLoad.Core.Features.ResourceExecution;
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
        byte[]? payload = null;
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
            payload = OrleansSerialization.Serialize(original);
            var signed = codec.Issue(GrainRequestEnvelopeConstruction.Command(requestId, codec.Incarnation,
                principal.Id, OperationKind.MaintainAnnIndex, original.CommandId, payload,
                expiry < context.Now + context.Lifetime ? expiry : context.Now + context.Lifetime));
            using var identity = new GrainRequestIdentityScope(services, principal, requestId,
                original.CommandId, token, connectionId: connectionId);
            try
            {
                result = await ReadAsync(grains, signed, requestId, original, connectionId, token).ConfigureAwait(true);
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        Capture(() =>
        {
            if (payload is not null)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);
            }
        }, failures);
        AnnProjectionJobFailures.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
    }

    private static void Capture(Action cleanup, List<Exception> failures)
    {
        try
        { cleanup(); }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
    }

    private async Task<AnnMaintenanceResult> ReadAsync(global::Orleans.IGrainFactory grains, string signed,
        Guid requestId, AnnMaintenanceRequest original, Guid connectionId, CancellationToken token)
    {
        var actor = grains.GetGrain<IConnectionGrain>(connectionId);
        var purpose = new CqrsStreamPurpose(() => codec.Verify(signed));
        var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            streamToken => actor.ExecuteStreamAsync(signed, streamToken), serializer, requestId,
            clock, routing, purpose, token).ConfigureAwait(true);
        return Read(reply, original);
    }

    private static AnnMaintenanceResult Read(GrainOperationReply reply, AnnMaintenanceRequest original)
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? InvalidResult);
        }
        var value = GrainPayload.Read<GrainValue>(reply.Payload).Value;
        if (value is not AnnMaintenanceResult result || result.CommandId != original.CommandId
            || result.Consumer != original.Consumer || result.IndexGeneration != original.IndexGeneration
            || result.Phase != AnnMaintenancePhase.Completed)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
        }
        return result;
    }
}
