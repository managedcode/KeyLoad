using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Messaging;

namespace KeyLoad.Server;

internal sealed partial class BackgroundStorageOperations
{
    public Task<DueWorkDiscoveryResult> ReadDueWorkPageAsync(DueSweepCursor? cursor, CancellationToken cancellationToken)
    {
        return BackgroundStorageReadExecution.RunLocalAsync<DueWorkDiscoveryResult>(storage, token =>
        {
            token.ThrowIfCancellationRequested();
            var database = Database;
            try
            {
                var page = DueWorkDiscovery.ReadPage(database, cursor, clock.GetUtcNow(), token);
                return new DueWorkDiscoveryResult(page, page.Cursor, null);
            }
            catch (KeyLoadException failure) when (failure.Code == ErrorCode.Corruption
                && failure.Message == DueWorkProtocol.KeyExceedsBound)
            {
                return new DueWorkDiscoveryResult(null,
                    DueWorkCursor.DeferNext(database.Store.Identity, cursor), failure.Code);
            }

        }, cancellationToken);
    }

    public Task<QueueDeadlineDiscoveryResult> ReadQueueDeadlinePageAsync(QueueDeadlineCursor? cursor,
        CancellationToken cancellationToken)
    {
        return BackgroundStorageReadExecution.RunLocalAsync<QueueDeadlineDiscoveryResult>(storage, token =>
        {
            token.ThrowIfCancellationRequested();
            var database = Database;
            try
            {
                var page = QueueDeadlineDiscovery.ReadPage(database, cursor, clock.GetUtcNow(), token);
                return new QueueDeadlineDiscoveryResult(page, page.Cursor, null);
            }
            catch (KeyLoadException failure) when (failure.Code == ErrorCode.Corruption
                && failure.Message == DueWorkProtocol.KeyExceedsBound)
            {
                return new QueueDeadlineDiscoveryResult(null,
                    QueueDeadlineCursor.DeferNext(database.Store.Identity, cursor), failure.Code);
            }

        }, cancellationToken);
    }

    public async Task<RemoteTransferPendingPage?> ReadQueueTransferPendingAsync(RemoteTransferPendingCursor? cursor,
        CancellationToken cancellationToken)
    {
        var result = await BackgroundStorageReadExecution.RunLocalAsync<(bool HasValue, RemoteTransferPendingPage? Value)>(storage, token =>
        {
            token.ThrowIfCancellationRequested();
            return due.Value.TransferCoordinatorPrincipalId is { } subject
                ? (true, RemoteTransferPendingDiscovery.Read(Database, subject, cursor, token))
                : (false, default(RemoteTransferPendingPage?));

        }, cancellationToken);
        return result.Value;
    }

    public async Task<PrincipalRecord?> ReadDueCreatorAsync(DueWorkHint hint, CancellationToken cancellationToken)
    {
        var result = await BackgroundStorageReadExecution.RunLocalAsync<(bool HasValue, PrincipalRecord? Value)>(storage, token =>
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(hint);
            EngineResourceAccess.ValidatePartition(hint.Lane.Partition);
            if (hint.Kind == DueWorkKind.Saga)
            {
                var eligible = SagaTimeoutEligibility.Read(Database, hint, clock.GetUtcNow());
                return eligible.IsEligible && eligible.Creator is { } creator
                    ? (true, creator)
                    : (false, default(PrincipalRecord?));
            }
            return (true, Database.Store.Read(view =>
                Database.Principal(view, hint.CreatorPrincipalId, clock.GetUtcNow())));

        }, cancellationToken);
        return result.Value;
    }

    public Task<PrincipalRecord> ReadQueueDeadlineCreatorAsync(QueueDeadlineHint hint,
        CancellationToken cancellationToken)
    {
        return BackgroundStorageReadExecution.RunLocalAsync<PrincipalRecord>(storage, token =>
        {
            token.ThrowIfCancellationRequested();
            if (hint is null || hint.Lane?.Partition is null || hint.Mutation is null || hint.Mutation.Queue != hint.Lane.Queue)
            {
                throw Errors.Fail(ErrorCode.Validation, DueCoordinatorFields.InvalidHint);
            }
            EngineResourceAccess.ValidatePartition(hint.Lane.Partition);
            JsonData.Identifier(hint.Lane.Queue);
            var subject = due.Value.QueueDeadlinePrincipalId
                ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, QueueDeadlineCoordination.UnavailablePrincipal);
            return Database.Store.Read(view => Database.Principal(view, subject, clock.GetUtcNow()));

        }, cancellationToken);
    }

    public Task<PrincipalRecord> ReadTransferCoordinatorAsync(RemoteTransferCoordinationHint hint,
        CancellationToken cancellationToken)
    {
        return BackgroundStorageReadExecution.RunLocalAsync<PrincipalRecord>(storage, token =>
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(hint);
            if (hint.Source?.Partition is null)
            {
                throw Errors.Fail(ErrorCode.Validation, RemoteTransferCoordinationProtocol.InvalidHint);
            }
            var subject = due.Value.TransferCoordinatorPrincipalId
                ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferCoordinationProtocol.Unavailable);
            RemoteTransferCoordination.RequireHint(hint, hint.Source.Partition.AtomicPartitionId,
                subject, Database.Store.Identity.Incarnation);
            return Database.Store.Read(view => Database.Principal(view, subject, clock.GetUtcNow()));

        }, cancellationToken);
    }
}
