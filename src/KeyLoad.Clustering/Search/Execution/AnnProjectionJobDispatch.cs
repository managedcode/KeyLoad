using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal sealed class AnnProjectionJobDispatch(GrainRequestCodec codec, IServiceProvider services,
    TimeProvider clock, Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing)
{
    private const string InvalidResult = "The native ANN maintenance result is invalid.";

    internal async Task<AnnMaintenanceResult> ExecuteAsync(global::Orleans.IGrainFactory grains,
        PrincipalRecord principal, AnnMaintenanceRequest original, DateTimeOffset expiry, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var connectionId = NativeConnectionExecutionIdentity.Resolve(services);
        var failures = new List<Exception>();
        var cleanup = new List<Exception>();
        AnnMaintenanceResult? result = null;
        NativeRequestWorkChildReservation? reservation = null;
        byte[]? payload = null;
        try
        {
            payload = NativeSerialization.Serialize(original);
            var signed = codec.CreateAnnProjectionJobChild(requestId, principal.Id, original.CommandId, payload, expiry);
            var verified = codec.VerifyRequest(signed, requestId);
            if (binding is not INativeRequestWorkBinding native
                || !ReferenceEquals(binding.ResultRetention, native.OriginalProducer.ResultRetention))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
            }
            reservation = native.OriginalProducer.ReserveLocalChild(verified,
                services.GetRequiredService<PhysicalShardRecord>(), services.GetRequiredService<ReplicaSiloDiscoveryState>().Read());
            result = await AnnProjectionJobDispatchSettlement.RunOwnedAsync(services, principal, requestId,
                original.CommandId, connectionId, reservation,
                scopeToken => ReadAsync(grains, signed, requestId, original, connectionId, scopeToken), failures, cleanup, token).ConfigureAwait(true);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        await AnnProjectionJobDispatchSettlement.CompleteAsync(reservation, payload, failures, cleanup).ConfigureAwait(true);
        AnnProjectionJobFailures.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
    }

    private async Task<AnnMaintenanceResult> ReadAsync(global::Orleans.IGrainFactory grains, string signed,
        Guid requestId, AnnMaintenanceRequest original, Guid connectionId, CancellationToken token)
    {
        var actor = grains.GetGrain<IConnectionGrain>(connectionId);
        var purpose = new NativeCqrsStreamPurpose(() => codec.Verify(signed));
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
