using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal sealed partial class PartitionMovementSourceOwner
{
    public Task<PartitionMovementPageResult> ReadPageAsync(PrincipalRecord principal,
        PartitionMovePeerEnvelope verified, PartitionMovementPageQuery query, CancellationToken cancellationToken)
    {
        var entry = images.Find(principal, verified, query.HandleId, cancellationToken);
        return PartitionMovementSourcePageReader.Read(database, limits, clock, principal,
            verified, query, entry, cancellationToken);
    }

    public async Task<PartitionMovePhaseResult> ReleaseAsync(PrincipalRecord principal,
        PartitionMovePeerEnvelope verified, Guid handleId, CancellationToken cancellationToken)
    {
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        var entry = images.Find(principal, verified, handleId, cancellationToken);
        database.ValidateVerifiedPartitionMovementCaptureScope(principal.Id, verified, work);
        await entry.Session.DisposeAsync().ConfigureAwait(false);
        var result = await settle(verified, cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            if (closedMoves.Values.SelectMany(value => value).Any(value => value.HandleId == handleId))
            { throw Errors.Fail(ErrorCode.OwnershipLost, ClosedDetail); }
            entry.ReleaseWork();
            sessions.Remove(handleId);
        }
        return result;
    }

}
