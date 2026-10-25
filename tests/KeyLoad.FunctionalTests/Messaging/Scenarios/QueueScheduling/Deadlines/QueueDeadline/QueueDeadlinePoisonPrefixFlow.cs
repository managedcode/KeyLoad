using KeyLoad.Core.Messaging;
using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueDeadlinePoisonPrefixFlow
{
    private const string ScheduledMessage = QueueDeadlinePoisonPrefixProtocol.ScheduledMessage;
    private const string LeasedMessage = QueueDeadlinePoisonPrefixProtocol.LeasedMessage;
    private const string ExpiringMessage = QueueDeadlinePoisonPrefixProtocol.ExpiringMessage;
    private const int OversizedCharacters = 4_100;
    private const int MaximumPages = 12;
    private const int DueSeconds = QueueDeadlinePoisonPrefixProtocol.LeaseSeconds;

    internal static async Task RunAsync(bool leased, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        fixture.Configure(RuntimeQueueDeadlineProtocol.Queue, ResourceKind.WorkQueue);
        var lane = new QueueLaneRef(fixture.Partition, RuntimeQueueDeadlineProtocol.Queue);
        var start = QueueWholeFlowStorage.Clock(fixture.Store);
        var due = start.AddSeconds(DueSeconds);
        QueueDeadlineOperations.ConfigureWorker(fixture.Database, lane.Partition, start,
            Capability.QueueConsume | Capability.QueueInspect | Capability.QueueAck, QueueDeadlinePoisonPrefixProtocol.First);
        var seeded = await QueueDeadlinePoisonPrefixSeed.RunAsync(fixture.Database, fixture.Store, lane, start, due, token);
        var family = leased ? QueueDeadlineRecordDecoder.Leased : QueueDeadlineRecordDecoder.Scheduled;
        var poison = KeyCodec.Encode(family, new string(QueueDeadlinePoisonPrefixProtocol.PoisonCharacter, OversizedCharacters));
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(poison, [QueueDeadlinePoisonPrefixProtocol.PoisonByte]);
            return true;
        });
        var blocked = leased ? LeasedMessage : ScheduledMessage;
        var healthy = leased ? ScheduledMessage : LeasedMessage;
        var blockedImage = fixture.Database.InspectMessage(QueueDeadlinePoisonPrefixProtocol.Root, lane, blocked);
        await QueueDeadlinePoisonPrefixAssertions.CursorAndCancellationAsync(fixture.Database, fixture.Store, due, token);
        var damaged = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        var cursor = QueueDeadlineCursor.Match(fixture.Store.Identity, null);
        if (leased)
        {
            cursor = cursor.Advance(DuePrefixCursor.Empty);
        }
        cursor = await RefuseAndDeferAsync(fixture.Database, fixture.Store, cursor, due, token);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(fixture.Store, damaged, position, identity);
        var outcomes = await AdvanceOthersAsync(fixture.Database, fixture.Store, cursor, lane, due, healthy, token);
        outcomes.AddRange(seeded);
        await Assert.That(fixture.Database.InspectMessage(QueueDeadlinePoisonPrefixProtocol.Root, lane, blocked)).IsEqualTo(blockedImage);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(poison))!.SequenceEqual(new byte[] { QueueDeadlinePoisonPrefixProtocol.PoisonByte })).IsTrue();
        var retained = QueueWholeFlowStorage.Bytes(fixture.Store);
        var retainedPosition = fixture.Store.Position;
        token.ThrowIfCancellationRequested();
        fixture.Store.Dispose();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var cold = QueueWholeFlowStorage.Open(first);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, retained, retainedPosition, identity);
        await Assert.That(cold.InspectMessage(QueueDeadlinePoisonPrefixProtocol.Root, lane, blocked)).IsEqualTo(blockedImage);
        var selected = QueueDeadlineCursor.Match(first.Identity, null);
        if (leased)
        {
            selected = selected.Advance(DuePrefixCursor.Empty);
        }
        await RefuseAndDeferAsync(cold, first, selected, due, token);
        first.Commit((tx, _) =>
        {
            tx.Delete(poison);
            return true;
        });
        await ReplayAsync(cold, first, outcomes, token);
        var repaired = await AdvanceOthersAsync(cold, first, null, lane, due, blocked, token);
        await Assert.That(repaired.Count).IsEqualTo(QueueDeadlinePoisonPrefixProtocol.First);
        outcomes.AddRange(repaired);
        await QueueDeadlinePoisonPrefixAssertions.FinalAsync(cold, lane, due, leased);
        await FinalColdAsync(first, fixture.Directory, lane, due, leased, outcomes, identity, token);
    }

    private static async Task FinalColdAsync(ZoneTreeStore first, string directory, QueueLaneRef lane,
        DateTimeOffset due, bool leased, List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes,
        byte[] identity, CancellationToken token)
    {
        var complete = QueueWholeFlowStorage.Bytes(first);
        var finalPosition = first.Position;
        first.Dispose();
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var final = QueueWholeFlowStorage.Open(second);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(second, complete, finalPosition, identity);
        await ReplayAsync(final, second, outcomes, token);
        await QueueDeadlinePoisonPrefixAssertions.FinalAsync(final, lane, due, leased);
        await QueueDeadlinePoisonPrefixHealthy.RunAsync(final, second, directory, lane, due, leased, outcomes, token);
    }

    private static async Task<QueueDeadlineCursor> RefuseAndDeferAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueDeadlineCursor cursor, DateTimeOffset due, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => QueueDeadlineDiscovery.ReadPage(database, cursor, due, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(DueWorkProtocol.KeyExceedsBound);
        var deferred = QueueDeadlineCursor.DeferNext(store.Identity, cursor);
        await Assert.That(deferred.Next).IsEqualTo((cursor.Next + QueueDeadlinePoisonPrefixProtocol.First) % QueueDeadlinePoisonPrefixProtocol.FamilyCount);
        await Assert.That(deferred.Scheduled).IsEqualTo(cursor.Scheduled);
        await Assert.That(deferred.Leased).IsEqualTo(cursor.Leased);
        await Assert.That(deferred.Metadata).IsEqualTo(cursor.Metadata);
        return deferred;
    }

    private static async Task<List<(Command<DatabaseCommand> Operation, Result<object> Result)>> AdvanceOthersAsync(
        DatabaseEngine database, ZoneTreeStore store, QueueDeadlineCursor? cursor, QueueLaneRef lane,
        DateTimeOffset due, string target, CancellationToken token)
    {
        var outcomes = new List<(Command<DatabaseCommand> Operation, Result<object> Result)>();
        for (var pageIndex = QueueDeadlinePoisonPrefixProtocol.Initial; pageIndex < MaximumPages; pageIndex++)
        {
            token.ThrowIfCancellationRequested();
            QueueDeadlinePage page;
            try
            {
                page = QueueDeadlineDiscovery.ReadPage(database, cursor, due, token);
            }
            catch (KeyLoadException failure) when (failure.Code == ErrorCode.Corruption && failure.Message == DueWorkProtocol.KeyExceedsBound)
            {
                await Assert.That(failure.Message).IsEqualTo(DueWorkProtocol.KeyExceedsBound);
                cursor = QueueDeadlineCursor.DeferNext(store.Identity, cursor);
                continue;
            }
            cursor = page.Cursor;
            await Assert.That(page.Rejected).IsEmpty();
            await Assert.That(page.ExaminedRecords).IsLessThanOrEqualTo(database.DueExecution.MaximumRecordsPerPage);
            await Assert.That(page.ExaminedBytes).IsLessThanOrEqualTo(database.DueExecution.MaximumRangeBytes);
            foreach (var hint in page.Jobs)
            {
                var command = new CommandRequest(Guid.NewGuid(), lane.Partition, [hint.Mutation]);
                var operation = database.PrepareQueueRetryOperation(database.OwnCommand(database.CreateCommand(OperationKind.Batch, command.CommandId, RuntimeQueueDeadlineProtocol.Worker, due, System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options))));
                var placement = database.ReadAtomicPartitionPlacement(QueueDeadlinePoisonPrefixProtocol.Root, lane.Partition);
                var expected = new CommitReceipt(command.CommandId, new(placement.Incarnation,
                    lane.Partition.AtomicPartitionId, checked(database.LastApplied + QueueDeadlinePoisonPrefixProtocol.First)), [new(QueueDeadlineProtocol.ReceiptKind, lane.Queue, hint.Mutation.MessageId, hint.Mutation.ExpectedStateVersion + QueueDeadlinePoisonPrefixProtocol.First)], DurabilityProfile.ProcessDurable, 1);
                var result = database.Apply(operation);
                await Assert.That(Errors.Code(result.Problem).Value).IsNull();

                await Assert.That(OrleansSerialization.Serialize(result.Get<CommitReceipt>()).SequenceEqual(
                    OrleansSerialization.Serialize(expected))).IsTrue();
                outcomes.Add((operation, result));
            }
            if (outcomes.Any(item => item.Result.Get<CommitReceipt>().Mutations.Any(mutation => mutation.Id == target))
                && database.InspectMessage(QueueDeadlinePoisonPrefixProtocol.Root, lane, ExpiringMessage)!.Metadata.State == MessageState.Expired)
            {
                return outcomes;
            }
        }
        throw new InvalidOperationException(QueueDeadlinePoisonPrefixProtocol.NoProgress);
    }

    private static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        foreach (var item in outcomes)
        {
            token.ThrowIfCancellationRequested();
            await ReplayResultAssertions.Same<CommitReceipt>(database.Apply(item.Operation), item.Result);
            await ReplayResultAssertions.Same<CommitReceipt>(database.ResolveOutcome(item.Operation), item.Result);
        }
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
    }
}
