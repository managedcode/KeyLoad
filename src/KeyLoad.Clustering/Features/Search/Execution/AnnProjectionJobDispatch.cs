using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal sealed class AnnProjectionJobDispatch(GrainRequestCodec codec, IServiceProvider services,
    TimeProvider clock, Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing)
{
    private const string InvalidResult = "The native ANN maintenance result is invalid.";

    internal async Task<AnnMaintenanceResult> ExecuteAsync(global::Orleans.IGrainFactory grains,
        PrincipalRecord principal, AnnMaintenanceRequest original, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var connectionId = NativeConnectionExecutionIdentity.Resolve(services);
        var failures = new List<Exception>();
        AnnMaintenanceResult? result = null;
        try
        {
            using var identity = new GrainRequestIdentityScope(services, principal, requestId,
                original.CommandId, token, connectionId: connectionId);
            try
            {
                var payload = NativeSerialization.Serialize(original);
                var signed = codec.CreateCommand(requestId, principal.Id, OperationKind.MaintainAnnIndex,
                    original.CommandId, payload);
                var actor = grains.GetGrain<IConnectionGrain>(connectionId);
                var purpose = new NativeCqrsStreamPurpose(() => codec.Verify(signed));
                var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
                    streamToken => actor.ExecuteStreamAsync(signed, streamToken), serializer, requestId,
                    clock, routing, purpose, token).ConfigureAwait(true);
                result = Read(reply, original);
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        AnnProjectionJobFailures.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
    }

    private static AnnMaintenanceResult Read(GrainOperationReply reply, AnnMaintenanceRequest original)
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? InvalidResult);
        }
        var value = GrainNativePayload.Read<GrainValue>(reply.Payload).Value;
        if (value is not AnnMaintenanceResult result || result.CommandId != original.CommandId
            || result.Consumer != original.Consumer || result.IndexGeneration != original.IndexGeneration
            || result.Phase != AnnMaintenancePhase.Completed)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
        }
        return result;
    }
}
