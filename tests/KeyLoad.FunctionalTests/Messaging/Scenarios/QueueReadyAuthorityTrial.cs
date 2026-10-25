using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueReadyAuthorityTrial
{
    internal static async Task CounterAsync(QueueReadyAuthorityFault fault, bool expired, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure("jobs", ResourceKind.WorkQueue);
        var lane = new QueueLaneRef(fixture.Partition, "jobs");
        var time = QueueWholeFlowStorage.Clock(fixture.Store).AddSeconds(1);
        var producer = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new EnqueueMessage("jobs", "input", "{\"input\":1}", "{\"kind\":\"job\"}",
                ExpiresAt: expired ? time.AddSeconds(1) : null)]);
        var produced = QueueWholeFlowStorage.Apply(fixture.Database, OperationKind.Batch, producer, producer.CommandId, time);
        produced.Get<CommitReceipt>();
        await Assert.That(fixture.Database.InspectMessage("root", lane, "input")).IsEqualTo(new MessageInspection(
            new("input", MessageState.Ready, 0, 1, 1, null, expired ? time.AddSeconds(1) : null),
            "{\"input\":1}", "{\"kind\":\"job\"}"));
        var request = new ReceiveRequest(Guid.NewGuid(), lane);
        var operation = fixture.Database.OwnCommand(fixture.Database.CreateCommand(OperationKind.Receive, request.RequestId, "root", time.AddSeconds(2), System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options)));
        var original = QueueWholeFlowStorage.Bytes(fixture.Store);
        var (key, row) = DamageAuthorityRow(fixture, lane, fault);
        var detail = IsNumericReadyFault(fault)
            ? "The recorded queue retry decision no longer matches its input."
            : "The queue lifecycle accounting is invalid.";
        var damaged = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        await CounterRefusalAsync(fixture.Database, fixture.Store, operation, lane, damaged, position, identity, detail);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var first = new global::KeyLoad.Storage.ZoneTree.ZoneTreeStore(new(fixture.Directory),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(first);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, damaged, position, identity);
        await CounterRefusalAsync(database, first, operation, lane, damaged, position, identity, detail);
        first.Commit((tx, _) => { tx.Put(key, row); return true; });
        await Assert.That(QueueWholeFlowStorage.Bytes(first)).IsEquivalentTo(original,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var received = database.Apply(operation);
        var repairedImage = QueueWholeFlowStorage.Bytes(first);
        var repairedPosition = first.Position;
        await global::KeyLoad.FunctionalTests.ResourceExecution.ReplayResultAssertions.Same<ReceiveResult>(database.Apply(operation), received);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, repairedImage, repairedPosition, identity);
        await QueueReadyAuthorityColdContinuation.RunAsync(database, first, fixture.Directory, lane, received.Get<ReceiveResult>(), expired,
            operation, producer, produced, identity, token);
    }

    private static bool IsNumericReadyFault(QueueReadyAuthorityFault fault)
        => fault is QueueReadyAuthorityFault.NegativeAttempts or QueueReadyAuthorityFault.NonPositiveStateVersion
            or QueueReadyAuthorityFault.NegativeLeaseVersion or QueueReadyAuthorityFault.NonPositiveDeliveryGeneration;

    private static (byte[] Key, byte[] Original) DamageAuthorityRow(TestDatabase fixture,
        QueueLaneRef lane, QueueReadyAuthorityFault fault)
    {
        if (IsNumericReadyFault(fault))
        {
            return QueueReadyAuthorityFaultRow.Damage(fixture, lane, fault);
        }
        var key = global::KeyLoad.Core.KeySpace.Partition("queue-counters", lane.Partition, lane.Queue);
        var row = fixture.Store.Commit((tx, _) =>
        {
            var bytes = tx.ReadOwnedValue(key) ?? throw new InvalidOperationException("Original queue counters are absent.");
            global::KeyLoad.Storage.StorageRecords.PutRecord(tx, key, CorruptCounter(OrleansSerialization.Deserialize<QueueCounters>(bytes), fault));
            return bytes;
        });
        return (key, row);
    }

    private static QueueCounters CorruptCounter(QueueCounters counters, QueueReadyAuthorityFault fault)
        => fault switch
        {
            QueueReadyAuthorityFault.NegativeInFlightMessages => counters with { InFlightMessages = -1 },
            QueueReadyAuthorityFault.NegativeInFlightBytes => counters with { InFlightBytes = -1 },
            QueueReadyAuthorityFault.ExcessInFlightMessages => counters with { InFlightMessages = counters.StoredMessages + 1 },
            QueueReadyAuthorityFault.ExcessInFlightBytes => counters with { InFlightBytes = counters.StoredBytes + 1 },
            QueueReadyAuthorityFault.NegativeReadySequence => counters with { NextReadySequence = -1 },
            QueueReadyAuthorityFault.NegativeOrderSequence => counters with { NextOrderSequence = -1 },
            QueueReadyAuthorityFault.InFlightCountWithoutBytes => counters with { InFlightMessages = 1 },
            QueueReadyAuthorityFault.InFlightBytesWithoutCount => counters with { InFlightBytes = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };

    private static async Task CounterRefusalAsync(global::KeyLoad.Core.DatabaseEngine database,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, Command<DatabaseCommand> operation, QueueLaneRef lane,
        string[] image, long position, byte[] identity, string detail)
    {
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(operation));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
            await Assert.That(failure.Message).IsEqualTo(detail);
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            var outcome = store.Read(view => view.GetRecord<global::KeyLoad.Core.StoredOutcome>(
                global::KeyLoad.Core.KeySpace.PartitionOutcome(lane.Partition, "root", operation.CommandId)));
            await Assert.That(outcome).IsNull();
        }
    }

    internal static async Task RunAsync(QueueReadyAuthorityFault fault, bool expired, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        fixture.Configure("jobs", ResourceKind.WorkQueue);
        fixture.Configure("orders", ResourceKind.Collection);
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("worker", "tenant",
            [new("database", "jobs", Capability.QueueConsume | Capability.QueueAck),
                new("database", "orders", Capability.DocumentsWrite)], []))).Get<PrincipalRecord>();
        var now = QueueWholeFlowStorage.Clock(fixture.Store).AddSeconds(1);
        var producer = Guid.NewGuid();
        fixture.Submit(OperationKind.Batch, new CommandRequest(producer, fixture.Partition,
            [new EnqueueMessage("jobs", "input", "{\"input\":1}", ExpiresAt: expired ? now.AddSeconds(1) : null)]),
            id: producer, time: now).Get<CommitReceipt>();
        var lane = new QueueLaneRef(fixture.Partition, "jobs");
        var id = Guid.NewGuid();
        var original = fixture.Database.CreateCommand(OperationKind.Receive, id, "worker", now.AddSeconds(2),
            OrleansSerialization.Serialize(new ReceiveRequest(id, lane)));
        var input = OrleansSerialization.Serialize(original);
        var row = QueueReadyAuthorityFaultRow.Damage(fixture, lane, fault);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        var cut = fixture.ReadCanonicalCut();
        var failures = new List<Exception>();
        for (var repeat = 0; repeat < 2; repeat++)
        {
            token.ThrowIfCancellationRequested();
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Apply(original));
            failures.Add(failure);
            await QueueReadyAuthorityRefusalAssertions.RequireAsync(fixture, original, image, position, identity);
            await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(cut);
            await Assert.That(OrleansSerialization.Serialize(original).AsSpan().SequenceEqual(input)).IsTrue();
        }
        await QueueReadyAuthorityRefusalAssertions.RequireOriginalFailuresAsync(failures,
            QueueReadyAuthorityRefusalAssertions.ExpectedError(fault));
        QueueReadyAuthorityFaultRow.Restore(fixture, row.Key, row.Original);
        await Assert.That(fixture.ReadCanonicalCut().AppliedPosition).IsEqualTo(cut.AppliedPosition);
        var receive = fixture.Database.Apply(original).Get<ReceiveResult>();
        await Assert.That(OrleansSerialization.Serialize(original).AsSpan().SequenceEqual(input)).IsTrue();
        await QueueReadyAuthorityRefusalAssertions.RequireRepairedReplayAsync(fixture, original, receive);
        await QueueReadyAuthorityHealthy.CompleteOriginalAsync(fixture, lane, receive, expired, now.AddSeconds(2));
        await QueueReadyAuthorityHealthy.RunAsync(fixture, lane, now.AddSeconds(2), token);
        await ReopenCompletedAsync(fixture, original, receive, expired, token);
    }

    private static async Task ReopenCompletedAsync(TestDatabase fixture, Command<DatabaseCommand> original,
        ReceiveResult receive, bool expired, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        var cut = fixture.ReadCanonicalCut();
        fixture.JoinOwnedResources();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            token.ThrowIfCancellationRequested();
            using var cold = new global::KeyLoad.Storage.ZoneTree.ZoneTreeStore(new(fixture.Directory),
                fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            var owner = fixture.CreateConfiguredDatabase(cold);
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
            var stored = OutcomeStoreOracle.Read(cold, original)
                ?? throw new InvalidOperationException("The repaired original receive outcome is absent.");
            await global::KeyLoad.FunctionalTests.ResourceExecution.ReplayResultAssertions.Same<ReceiveResult>(
                stored, ManagedCode.Communication.Result<object>.Succeed(receive));
            var replay = owner.Apply(original);
            if (expired)
            {
                await global::KeyLoad.FunctionalTests.ResourceExecution.ReplayResultAssertions.Same<ReceiveResult>(replay, stored);
            }
            else
            {
                await Assert.That(Errors.Code(replay.Problem).Value).IsEqualTo(ErrorCode.StaleLease);
                await Assert.That(replay.Value).IsNull();
            }
            await Assert.That(owner.LastApplied).IsEqualTo(cut.AppliedPosition);
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        }
    }
}
