using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineNativeTrial
{
    internal static async Task RunAsync(QueueDeadlineKind kind, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(QueueDeadlineNativeProtocol.Queue, ResourceKind.WorkQueue);
        var start = QueueWholeFlowStorage.Clock(fixture.Store);
        var due = start.AddSeconds(QueueDeadlineNativeProtocol.DueSeconds);
        var lane = new QueueLaneRef(fixture.Partition, QueueDeadlineNativeProtocol.Queue);
        QueueDeadlineNativeOperations.ConfigureWorker(fixture.Database, fixture.Partition, start,
            Capability.QueueConsume | Capability.QueueInspect | Capability.QueueAck, QueueDeadlineNativeProtocol.First);
        var expiry = kind == QueueDeadlineKind.ExpireMessage ? due : (DateTimeOffset?)null;
        var notBefore = kind == QueueDeadlineKind.ExpireMessage ? null : (DateTimeOffset?)due;
        var seed = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new EnqueueMessage(lane.Queue, QueueDeadlineNativeProtocol.Message, QueueDeadlineNativeProtocol.Payload,
                QueueDeadlineNativeProtocol.Headers, NotBefore: notBefore, ExpiresAt: expiry)]);
        var seeded = QueueDeadlineNativeOperations.Batch(fixture.Database, seed, start, QueueDeadlineNativeProtocol.Root);
        seeded.Get<CommitReceipt>();
        var metadata = new MessageMetadata(QueueDeadlineNativeProtocol.Message,
            notBefore is null ? MessageState.Ready : MessageState.Scheduled, (int)QueueDeadlineNativeProtocol.Initial,
            QueueDeadlineNativeProtocol.First, notBefore is null ? QueueDeadlineNativeProtocol.First : QueueDeadlineNativeProtocol.Initial,
            notBefore, expiry);
        await QueueDeadlineNativeAssertions.OriginalAsync(fixture.Database, lane, metadata);
        if (kind == QueueDeadlineKind.ExpireLease)
        { (metadata, due) = await QueueDeadlineNativeLease.CreateAsync(fixture.Database, lane, metadata, due); }
        var advance = new AdvanceQueueDeadline(lane.Queue, metadata.Id, metadata.State, metadata.StateVersion,
            metadata.LeaseVersion, due, kind);
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition, [advance]);
        await QueueDeadlineNativeAssertions.RefusedAsync(fixture.Database, fixture.Store, lane, command,
            due.AddTicks(QueueDeadlineNativeProtocol.PreviousTick), ErrorCode.Conflict);
        command = command with { CommandId = Guid.NewGuid() };
        var stale = command with { CommandId = Guid.NewGuid(), Mutations = [advance with { ExpectedStateVersion = metadata.StateVersion + QueueDeadlineNativeProtocol.First }] };
        await QueueDeadlineNativeAssertions.RefusedAsync(fixture.Database, fixture.Store, lane, stale, due, ErrorCode.RevisionConflict);
        await QueueDeadlineNativeDiscovery.RequireAsync(fixture, lane, advance, due, token);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(first);
        await QueueDeadlineNativeAssertions.OriginalAsync(database, lane, metadata);
        await QueueDeadlineNativeContinuation.RunAsync(database, first, fixture.Directory, lane, command, seeded,
            seed, metadata, due, token);
    }
}
