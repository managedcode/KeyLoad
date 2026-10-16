using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.Search;

internal static class AnnProjectionNativeLifetimeQuery
{
    private const int NoReplyBytes = 0;
    internal static async Task<AnnProjectionObservationResult> ExecuteAsync(OrleansNode node,
        PartitionHost partition, TimeProvider clock, PrincipalRecord principal,
        LocalAnnObservationFence admitted, AnnProjectionNativeCallV2 call, byte[] payload, ReadExecutionBudget work,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateAnnProjectionObservationV2(
            requestId, principal.Id, payload, call.Original.ExpiresAt);
        var nativeReplyBytes = NoReplyBytes;
        var result = await NativeLifetimeReadChildExecution.ExecuteAsync<AnnProjectionObservationResult>(node,
            principal, requestId, signed, payload, binding, cancellationToken,
            reply => nativeReplyBytes = reply.Payload.Length,
            node.RuntimeServices.GetRequiredService<NativeConnectionOwnerIdentity>().Id).ConfigureAwait(false);
        var current = partition.Database.Store.Read(view =>
            partition.Database.Principal(work.CreateView(view), principal.Id, clock.GetUtcNow()));
        if (current.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid);
        }
        var observation = partition.Database.CaptureLocalAnnObservationBound(principal.Id,
            call.Original.Observation.OriginalRequest, work,
            new(call.Definition, call.Coverage, call.Principal, call.Definitions), binding);
        if (observation != admitted)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid);
        }
        work.ChargeBytes(nativeReplyBytes);
        if (result.AcceptedReadBytes < AnnProjectionObservationProtocol.NoReadWork
            || result.AcceptedReadBytes > call.Original.Observation.MaximumReadBytes
            || result.ExaminedRecords < AnnProjectionObservationProtocol.NoReadWork
            || result.ExaminedRecords > call.Original.Observation.MaximumRecords)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
        }
        work.ImportNativeObservation(result.AcceptedReadBytes, result.ExaminedRecords);
        work.Check();
        return result;
    }
}
