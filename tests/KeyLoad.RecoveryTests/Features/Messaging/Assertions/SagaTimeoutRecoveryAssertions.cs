using System.Globalization;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class SagaTimeoutRecoveryAssertions
{
    private static readonly TimeSpan TimeoutTtl = TimeSpan.FromDays(1);

    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken cancellationToken)
    {
        using var store = new ZoneTreeStore(new(root));
        var database = new DatabaseEngine(store, new AuthorizationPolicy());
        var operation = await ReadOperationAsync(root, cancellationToken);
        var seedTail = await ReadSeedTailAsync(root, cancellationToken);
        var recoveredState = Inspect(database);
        await AssertDeadlineReachedAsync(recoveredState, operation);
        var recoveredOutcome = await AssertAtomicCutAsync(store, database, operation, seedTail, recoveredState, stage);
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        await AssertCommittedAsync(receipt, recoveredOutcome, database, store, seedTail, recoveredState);
        await AssertStableRetryAsync(database, store, operation, receipt, seedTail);
    }

    private static async Task<ReplicatedOperation> ReadOperationAsync(string root, CancellationToken cancellationToken)
        => JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, SagaTimeoutCrashScenario.OperationFile), cancellationToken));

    private static async Task<long> ReadSeedTailAsync(string root, CancellationToken cancellationToken)
        => long.Parse(await File.ReadAllTextAsync(Path.Combine(root, SagaTimeoutCrashScenario.SeedTailFile),
            cancellationToken), CultureInfo.InvariantCulture);

    private static SagaInspection Inspect(DatabaseEngine database)
        => database.InspectSaga(CrashFixtureValues.Principal,
            new(SagaTimeoutCrashScenario.Partition, SagaTimeoutCrashScenario.Queue),
            Guid.Parse(SagaTimeoutCrashScenario.SagaIdText))!;

    private static async Task AssertDeadlineReachedAsync(SagaInspection recovered, ReplicatedOperation operation)
    {
        var waiting = recovered.Phase == SagaPhase.Waiting
            && recovered.Revision == SagaTimeoutCrashScenario.WaitingRevision;
        var timedOut = recovered.Phase == SagaPhase.TimedOut
            && recovered.Revision == SagaTimeoutCrashScenario.TimedOutRevision;
        await Assert.That(waiting || timedOut).IsTrue();
        await Assert.That(recovered.Deadline).IsNotNull();
        await Assert.That(operation.EvaluatedAt).IsGreaterThanOrEqualTo(recovered.Deadline!.Value);
    }

    private static async Task<CommitReceipt?> AssertAtomicCutAsync(ZoneTreeStore store, DatabaseEngine database,
        ReplicatedOperation operation, long seedTail, SagaInspection waiting, CommitStage stage)
    {
        var timeoutLane = new QueueLaneRef(SagaTimeoutCrashScenario.Partition, SagaTimeoutCrashScenario.TimeoutQueue);
        var message = database.InspectMessage(CrashFixtureValues.Principal, timeoutLane,
            SagaTimeoutCrashScenario.TimeoutMessageId());
        var outcome = database.Outcome(CrashFixtureValues.Principal, operation.Id)?.Get<CommitReceipt>();
        var entry = ReadEntry(store, seedTail + 1);
        var tail = database.GetOutboxStatus(CrashFixtureValues.Principal, SagaTimeoutCrashScenario.Partition).Head.Tail;
        var recovered = Inspect(database);
        var committed = recovered.Phase == SagaPhase.TimedOut
            && recovered.Revision == SagaTimeoutCrashScenario.TimedOutRevision;
        var isWaiting = recovered.Phase == SagaPhase.Waiting
            && recovered.Revision == SagaTimeoutCrashScenario.WaitingRevision;
        await Assert.That(committed || isWaiting).IsTrue();
        await Assert.That(message is not null).IsEqualTo(committed);
        await Assert.That(outcome is not null).IsEqualTo(committed);
        await Assert.That(entry is not null).IsEqualTo(committed);
        await Assert.That(tail).IsEqualTo(seedTail + (committed ? 1 : 0));
        if (committed)
        {
            await Assert.That(entry!.Sequence).IsEqualTo(seedTail + 1);
            await Assert.That(entry.Mutation).IsTypeOf<ExpireSaga>();
            await Assert.That(entry.Receipt.Kind).IsEqualTo("expireSaga");
            await Assert.That(entry.Receipt.Revision).IsEqualTo(SagaTimeoutCrashScenario.TimedOutRevision);
            await Assert.That(entry.Commit).IsEqualTo(outcome!.Token);
        }
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(committed).IsTrue();
        }
        await Assert.That(waiting.Deadline).IsEqualTo(recovered.Deadline);
        var counters = ReadQueueCounters(store, timeoutLane);
        await Assert.That(counters.StoredMessages).IsEqualTo(committed ? 1L : 0L);
        return outcome;
    }

    private static async Task AssertCommittedAsync(CommitReceipt receipt, CommitReceipt? recoveredOutcome,
        DatabaseEngine database, ZoneTreeStore store, long seedTail, SagaInspection waiting)
    {
        var saga = Inspect(database);
        var timeoutLane = new QueueLaneRef(SagaTimeoutCrashScenario.Partition, SagaTimeoutCrashScenario.TimeoutQueue);
        var message = database.InspectMessage(CrashFixtureValues.Principal, timeoutLane,
            SagaTimeoutCrashScenario.TimeoutMessageId());
        var entry = ReadEntry(store, seedTail + 1);
        await Assert.That(saga.Phase).IsEqualTo(SagaPhase.TimedOut);
        await Assert.That(saga.Revision).IsEqualTo(SagaTimeoutCrashScenario.TimedOutRevision);
        await Assert.That(saga.StateJson).IsEqualTo(SagaTimeoutCrashScenario.StateJson);
        await Assert.That(saga.Deadline).IsEqualTo(waiting.Deadline);
        await Assert.That(message).IsNotNull();
        await Assert.That(message!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(message.Metadata.ExpiresAt).IsEqualTo(waiting.Deadline!.Value.Add(TimeoutTtl));
        await Assert.That(message.PayloadJson).IsEqualTo(SagaTimeoutCrashScenario.TimeoutPayload);
        await Assert.That(message.HeadersJson).IsEqualTo(SagaTimeoutCrashScenario.TimeoutHeaders);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0].Kind).IsEqualTo("expireSaga");
        await Assert.That(entry!.Commit).IsEqualTo(receipt.Token);
        await Assert.That(database.GetOutboxStatus(CrashFixtureValues.Principal,
            SagaTimeoutCrashScenario.Partition).Head.Tail).IsEqualTo(seedTail + 1);
        await Assert.That(ReadQueueCounters(store, timeoutLane).StoredMessages).IsEqualTo(1L);
        if (recoveredOutcome is not null)
        {
            await Assert.That(receipt.Token).IsEqualTo(recoveredOutcome.Token);
            await Assert.That(receipt.Mutations[0]).IsEqualTo(recoveredOutcome.Mutations[0]);
        }
    }

    private static async Task AssertStableRetryAsync(DatabaseEngine database, ZoneTreeStore store,
        ReplicatedOperation operation, CommitReceipt first, long seedTail)
    {
        var timeoutLane = new QueueLaneRef(SagaTimeoutCrashScenario.Partition, SagaTimeoutCrashScenario.TimeoutQueue);
        var repeated = database.Apply(operation).Get<CommitReceipt>();
        var resolved = database.ResolveOutcome(operation).Get<CommitReceipt>();
        await Assert.That(repeated.Token).IsEqualTo(first.Token);
        await Assert.That(resolved.Token).IsEqualTo(first.Token);
        await Assert.That(repeated.Mutations[0]).IsEqualTo(first.Mutations[0]);
        await Assert.That(database.GetOutboxStatus(CrashFixtureValues.Principal,
            SagaTimeoutCrashScenario.Partition).Head.Tail).IsEqualTo(seedTail + 1);
        await Assert.That(ReadQueueCounters(store, timeoutLane).StoredMessages).IsEqualTo(1L);
    }

    private static QueueCounters ReadQueueCounters(ZoneTreeStore store, QueueLaneRef lane)
        => store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            SagaTimeoutCrashScenario.QueueCountersSpace, lane.Partition, lane.Queue))) ?? new(0, 0, 0, 0, 0);

    private static OutboxEntry? ReadEntry(ZoneTreeStore store, long sequence)
        => store.Read(view => view.GetRecord<OutboxEntry>(KeySpace.Partition(
            SagaTimeoutCrashScenario.OutboxSpace, SagaTimeoutCrashScenario.Partition, sequence)));
}
