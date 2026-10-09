using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal sealed partial class PartitionMovementTransferSourceReads
{
    public Task<PartitionMovementPageResult> ReadPageAsync(PrincipalRecord principal,
        PartitionMovementTransferDataCapability query, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        query = PartitionMovementTransferSourceScope.Own(query, database.Limits, PartitionMovementTransferDataAction.Page);
        var entry = Find(principal, query, cancellationToken);
        work.Check();
        database.ValidatePartitionMovementTransferReadSession(principal.Id, query.OriginalAuthorityReplyBytes,
            query.OriginalAuthoritySignature, work);
        PartitionMovementPageResult? result = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            using var page = entry.Session.Borrow(query.Ordinal, cancellationToken);
            using var stage = work.EnterStageCancellation(entry.Session.StageCancellation);
            ServerFailureObserver.Observe(() =>
            {
                work.CheckResult(page.Page);
                var encoded = NativeSerialization.Serialize(page.Page);
                result = new(query.HandleId, query.Ordinal, encoded);
                work.CheckResult(result);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return Task.FromResult(result ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage));
    }

    public async Task<PartitionMovementTransferClosed> CloseAsync(PrincipalRecord principal,
        PartitionMovementTransferDataCapability query, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        query = PartitionMovementTransferSourceScope.Own(query, database.Limits, PartitionMovementTransferDataAction.Close);
        work.Check();
        var authority = database.VerifyPartitionMovementTransferCleanupAuthority(principal.Id,
            query.OriginalAuthorityReplyBytes, query.OriginalAuthoritySignature, work);
        PartitionMovementTransferSourceEntry? entry;
        lock (gate)
        {
            requireOpen();
            RequireNoAbortingHandle(query.HandleId);
            if (!sessions.TryGetValue(query.HandleId, out var retained))
            { return new(query.HandleId, PartitionMovementTransferCloseDisposition.AlreadyAbsent); }
            entry = retained as PartitionMovementTransferSourceEntry
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
            if (authority.Header.Partition != entry.Partition || authority.Header.MoveId != entry.MoveId)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            entry.Require(principal.Id, query);
        }
        await entry.Session.DisposeAsync().ConfigureAwait(false);
        lock (gate)
        {
            RequireNoAbortingHandle(query.HandleId);
            entry.ReleaseWork();
            sessions.Remove(query.HandleId);
        }
        return new(query.HandleId, PartitionMovementTransferCloseDisposition.Joined);
    }

    private void RequireNoAbortingHandle(Guid handleId)
    {
        if (closedMoves.Values.SelectMany(value => value).Any(value => value.HandleId == handleId))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }

    private PartitionMovementTransferSourceEntry Find(PrincipalRecord principal,
        PartitionMovementTransferDataCapability query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        lock (gate)
        {
            if (!sessions.TryGetValue(query.HandleId, out var retained) || retained is not PartitionMovementTransferSourceEntry entry)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            requireMoveOpen(entry.Partition, entry.MoveId);
            entry.Require(principal.Id, query);
            return entry;
        }
    }
}
