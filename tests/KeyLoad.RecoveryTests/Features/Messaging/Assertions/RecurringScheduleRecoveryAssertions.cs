using System.Globalization;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RecurringScheduleRecoveryAssertions
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken cancellationToken)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
        var operation = await ReadOperationAsync(root, cancellationToken);
        var seedTail = await ReadSeedTailAsync(root, cancellationToken);
        var inspection = Inspect(database);
        await AssertSeedAndLoggedDueAsync(inspection, operation);
        var recoveredOutcome = await AssertAtomicCutAsync(store, database, operation, seedTail, inspection, stage);
        var receipt = Retry(database, operation);
        await AssertReceiptAsync(receipt, recoveredOutcome, inspection, seedTail, database, store);
        await AssertStableRetryAsync(database, store, operation, receipt, seedTail);
    }

    private static async Task<ReplicatedOperation> ReadOperationAsync(string root, CancellationToken cancellationToken)
        => JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, RecurringScheduleCrashScenario.OperationFile), cancellationToken));

    private static async Task<long> ReadSeedTailAsync(string root, CancellationToken cancellationToken)
        => long.Parse(await File.ReadAllTextAsync(Path.Combine(root, RecurringScheduleCrashScenario.SeedTailFile),
            cancellationToken), CultureInfo.InvariantCulture);

    private static RecurringScheduleInspection Inspect(DatabaseEngine database)
        => database.InspectRecurringSchedule(CrashFixtureValues.Principal,
            new(RecurringScheduleCrashScenario.Partition, RecurringScheduleCrashScenario.Queue),
            Guid.Parse(RecurringScheduleCrashScenario.ScheduleIdText))!;

    private static async Task AssertSeedAndLoggedDueAsync(RecurringScheduleInspection inspection,
        ReplicatedOperation operation)
    {
        await Assert.That(inspection.Revision).IsEqualTo(1L);
        await Assert.That(inspection.Generation).IsEqualTo(RecurringScheduleCrashScenario.Generation);
        await Assert.That(inspection.Cancelled).IsFalse();
        await Assert.That(operation.EvaluatedAt).IsGreaterThanOrEqualTo(inspection.Definition.FirstDueAt);
        await Assert.That(inspection.Definition.FirstDueAt.AddDays(1)).IsGreaterThan(operation.EvaluatedAt);
    }

    private static async Task<CommitReceipt?> AssertAtomicCutAsync(ZoneTreeStore store, DatabaseEngine database,
        ReplicatedOperation operation, long seedTail, RecurringScheduleInspection inspection, CommitStage stage)
    {
        var lane = new QueueLaneRef(RecurringScheduleCrashScenario.Partition, RecurringScheduleCrashScenario.Queue);
        var message = database.InspectMessage(CrashFixtureValues.Principal, lane,
            RecurringScheduleCrashScenario.OccurrenceId(RecurringScheduleCrashScenario.Generation,
                RecurringScheduleCrashScenario.FirstOrdinal));
        var outcome = OutcomeStoreOracle.Read(database.Store, operation)?.Get<CommitReceipt>();
        var entry = ReadEntry(store, seedTail + 1);
        var tail = database.GetOutboxStatus(CrashFixtureValues.Principal, RecurringScheduleCrashScenario.Partition).Head.Tail;
        var committed = inspection.NextOrdinal == 1;
        await Assert.That(inspection.NextOrdinal is 0 or 1).IsTrue();
        await Assert.That(message is not null).IsEqualTo(committed);
        await Assert.That(outcome is not null).IsEqualTo(committed);
        await Assert.That(entry is not null).IsEqualTo(committed);
        await Assert.That(tail).IsEqualTo(seedTail + (committed ? 1 : 0));
        if (committed)
        {
            await Assert.That(entry!.Sequence).IsEqualTo(seedTail + 1);
            await Assert.That(entry.Mutation).IsTypeOf<EmitRecurringOccurrences>();
            await Assert.That(entry.Receipt.Kind).IsEqualTo("emitRecurringOccurrences");
            await Assert.That(entry.Receipt.Revision).IsEqualTo(1L);
            await Assert.That(entry.Commit).IsEqualTo(outcome!.Token);
        }
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(committed).IsTrue();
        }
        var counters = ReadQueueCounters(store, new(RecurringScheduleCrashScenario.Partition,
            RecurringScheduleCrashScenario.Queue));
        await Assert.That(counters.StoredMessages).IsEqualTo(committed ? 1L : 0L);
        return outcome;
    }

    private static CommitReceipt Retry(DatabaseEngine database, ReplicatedOperation operation)
        => database.Apply(operation).Get<CommitReceipt>();

    private static async Task AssertReceiptAsync(CommitReceipt receipt, CommitReceipt? recoveredOutcome,
        RecurringScheduleInspection beforeRetry, long seedTail, DatabaseEngine database, ZoneTreeStore store)
    {
        var schedule = Inspect(database);
        var lane = new QueueLaneRef(RecurringScheduleCrashScenario.Partition, RecurringScheduleCrashScenario.Queue);
        var message = database.InspectMessage(CrashFixtureValues.Principal, lane,
            RecurringScheduleCrashScenario.OccurrenceId(RecurringScheduleCrashScenario.Generation,
                RecurringScheduleCrashScenario.FirstOrdinal));
        var outbox = ReadEntry(store, seedTail + 1);
        await Assert.That(schedule.NextOrdinal).IsEqualTo(1L);
        await Assert.That(schedule.Definition.FirstDueAt).IsEqualTo(beforeRetry.Definition.FirstDueAt);
        await Assert.That(message).IsNotNull();
        await Assert.That(message!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(message.Metadata.NotBefore).IsEqualTo(schedule.Definition.FirstDueAt);
        await Assert.That(message.PayloadJson).IsEqualTo(RecurringScheduleCrashScenario.MessagePayload);
        await Assert.That(message.HeadersJson).IsEqualTo(RecurringScheduleCrashScenario.MessageHeaders);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0].Kind).IsEqualTo("emitRecurringOccurrences");
        await Assert.That(outbox!.Commit).IsEqualTo(receipt.Token);
        await Assert.That(database.GetOutboxStatus(CrashFixtureValues.Principal,
            RecurringScheduleCrashScenario.Partition).Head.Tail).IsEqualTo(seedTail + 1);
        await Assert.That(ReadQueueCounters(store, lane).StoredMessages).IsEqualTo(1L);
        if (recoveredOutcome is not null)
        {
            await Assert.That(receipt.Token).IsEqualTo(recoveredOutcome.Token);
            await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(recoveredOutcome, receipt);
        }
    }

    private static async Task AssertStableRetryAsync(DatabaseEngine database, ZoneTreeStore store,
        ReplicatedOperation operation, CommitReceipt first, long seedTail)
    {
        var repeated = database.Apply(operation).Get<CommitReceipt>();
        var resolved = database.ResolveOutcome(operation).Get<CommitReceipt>();
        await Assert.That(repeated.Token).IsEqualTo(first.Token);
        await Assert.That(resolved.Token).IsEqualTo(first.Token);
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(first, repeated);
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(first, resolved);
        await Assert.That(database.GetOutboxStatus(CrashFixtureValues.Principal,
            RecurringScheduleCrashScenario.Partition).Head.Tail).IsEqualTo(seedTail + 1);
        var lane = new QueueLaneRef(RecurringScheduleCrashScenario.Partition, RecurringScheduleCrashScenario.Queue);
        await Assert.That(ReadQueueCounters(store, lane).StoredMessages).IsEqualTo(1L);
    }

    private static QueueCounters ReadQueueCounters(ZoneTreeStore store, QueueLaneRef lane)
        => store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            RecurringScheduleCrashScenario.QueueCountersSpace, lane.Partition, lane.Queue))) ?? new(0, 0, 0, 0, 0);

    private static OutboxEntry? ReadEntry(ZoneTreeStore store, long sequence)
        => store.Read(view => view.GetRecord<OutboxEntry>(KeySpace.Partition(
            RecurringScheduleCrashScenario.OutboxSpace, RecurringScheduleCrashScenario.Partition, sequence)));
}
