using KeyLoad.Core;
using ManagedCode.Communication.Commands;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

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
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
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
        await global::KeyLoad.UnitTests.Features.ResourceExecution.NativeReplayResultAssertions.Same<ReceiveResult>(database.Apply(operation), received);
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
            global::KeyLoad.Storage.StorageRecords.PutRecord(tx, key, CorruptCounter(NativeSerialization.Deserialize<QueueCounters>(bytes), fault));
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
        var owningFailures = new List<Exception>();
        try
        {
            token.ThrowIfCancellationRequested();
            using var fixture = new TestDatabase();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
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
                var row = QueueReadyAuthorityFaultRow.Damage(fixture, lane, fault);
                var image = QueueWholeFlowStorage.Bytes(fixture.Store);
                var position = fixture.Store.Position;
                var identity = NativeSerialization.Serialize(fixture.Store.Identity);
                var id = Guid.NewGuid();
                var request = new ReceiveRequest(id, lane);
                var failures = new List<Exception>();
                var expectedError = QueueReadyAuthorityRefusalAssertions.ExpectedError(fault);
                var first = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Submit(OperationKind.Receive,
                    request, principal: "worker", id: id, time: now.AddSeconds(2)));
                failures.Add(first);
                await Assert.That(first.Code).IsEqualTo(ErrorCode.RecoveryRequired);
                var retained = fixture.ReadStoredOutcome(id);
                var original = retained.PreparedCommand ?? throw new InvalidOperationException("The failed native entry is absent.");
                await QueueReadyAuthorityRefusalAssertions.RequireAsync(fixture, original, image, position, identity);
                var cut = fixture.ReadCanonicalCut();
                var repeat = Assert.ThrowsExactly<KeyLoadException>(() => fixture.SubmitIssued(original));
                failures.Add(repeat);
                await Assert.That(repeat.Code).IsEqualTo(ErrorCode.RecoveryRequired);
                await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(cut);
                await QueueReadyAuthorityRefusalAssertions.RequireAsync(fixture, original, image, position, identity);
                var recovered = fixture.RecoverFaultOwnedRow(original,
                    () => QueueReadyAuthorityFaultRow.Restore(fixture, row.Key, row.Original), failures, token, expectedError);
                await Assert.That(recovered.Index).IsEqualTo(retained.CanonicalSequence);
                await Assert.That(recovered.Operation!.CommandId).IsEqualTo(id);
                await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(cut);
                await Assert.That(recovered.Operation!.Value!.Input.ToArray()).IsEquivalentTo(original.Value!.Input.ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
                await QueueReadyAuthorityRefusalAssertions.RequireOriginalFailuresAsync(failures, expectedError);
                var receive = fixture.SubmitIssued(original).Get<ReceiveResult>();
                await QueueReadyAuthorityRefusalAssertions.RequireRepairedReplayAsync(fixture, original, receive);
                await QueueReadyAuthorityHealthy.CompleteOriginalAsync(fixture, lane, receive, expired, now.AddSeconds(2));
                await QueueReadyAuthorityHealthy.RunAsync(fixture, lane, now.AddSeconds(2), token);
            }, owningFailures);
        }
        catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { owningFailures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { owningFailures.Add(error); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(owningFailures);
    }
}
