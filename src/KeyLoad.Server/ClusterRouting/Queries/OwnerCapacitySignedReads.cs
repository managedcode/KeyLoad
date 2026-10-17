using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class OwnerCapacitySignedReads(OrleansNode node, PartitionHost partition,
    TimeProvider clock)
{
    internal async Task<OwnerCapacitySnapshotV1> ReadAsync(OwnerCapacityReadRequestV1 request,
        string originalPrincipalId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var principal = partition.Database.Store.Read(view =>
            partition.Database.Principal(view, originalPrincipalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        if (principal.PolicyEpoch != request.OriginalPolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, TenantPlacementPolicyProtocol.ScopeMismatch);
        }
        partition.Database.VerifyPhysicalCommandOwner(request.Owner, false, token);
        var signed = node.CatalogRequestCodec().CreateOwnerCapacityRead(request.RequestId, principal.Id, request);
        var failures = new List<Exception>();
        GrainOperationReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = node.OpenRequestContext(principal, request.RequestId, Guid.Empty, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reply = await node.ExecuteAsync(request.RequestId, signed, command: false, token).ConfigureAwait(false);
            },
                failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        if (reply?.Error is { } code)
        {
            throw Errors.Fail(code, reply.SafeDetail ?? TenantPlacementCapacityProtocol.Unavailable);
        }
        return reply is not null && GrainNativePayload.Read<GrainValue>(reply.Payload).Value is OwnerCapacitySnapshotV1 value
            ? value : throw Errors.Fail(ErrorCode.Corruption, TenantPlacementCapacityProtocol.InvalidProof);
    }
}
