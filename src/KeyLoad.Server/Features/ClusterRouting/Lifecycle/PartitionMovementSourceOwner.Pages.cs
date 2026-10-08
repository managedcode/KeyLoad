using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal sealed partial class PartitionMovementSourceOwner
{
    public Task<PartitionMovementPageResult> ReadPageAsync(PrincipalRecord principal,
        PartitionMovePeerEnvelope verified, PartitionMovementPageQuery query, CancellationToken cancellationToken)
    {
        var entry = Find(principal, verified, query.HandleId, cancellationToken);
        return PartitionMovementSourcePageReader.Read(database, limits, clock, principal,
            verified, query, entry, cancellationToken);
    }

    public async Task<PartitionMovePhaseResult> ReleaseAsync(PrincipalRecord principal,
        PartitionMovePeerEnvelope verified, Guid handleId, CancellationToken cancellationToken)
    {
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        var entry = Find(principal, verified, handleId, cancellationToken);
        database.ValidateVerifiedPartitionMovementCaptureScope(principal.Id, verified, work);
        await entry.Session.DisposeAsync().ConfigureAwait(false);
        var result = await settle(verified, cancellationToken).ConfigureAwait(false);
        entry.ReleaseWork();
        lock (gate)
        { sessions.Remove(handleId); }
        return result;
    }

    private PartitionMovementSourceEntry Find(PrincipalRecord principal, PartitionMovePeerEnvelope verified,
        Guid handleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        lock (gate)
        {
            RequireMoveOpen(verified.Partition, verified.MoveId);
            if (!sessions.TryGetValue(handleId, out var entry))
            { throw Errors.Fail(ErrorCode.OwnershipLost, ClosedDetail); }
            entry.Require(verified, clock.GetUtcNow());
            return entry;
        }
    }
}
