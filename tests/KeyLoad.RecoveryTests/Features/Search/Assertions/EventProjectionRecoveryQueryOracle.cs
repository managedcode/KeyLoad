using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Search;
using KeyLoad.Query;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class EventProjectionRecoveryQueryOracle
{
    private const int ResultLimit = 10;
    private const int FusionConstant = 60;
    private const string InputReplacementJson = "\"updated source value\"";

    internal static async Task AssertSearchAsync(DatabaseEngine database, bool committed,
        CancellationToken cancellationToken)
    {
        var actual = await new SearchEngine(database, RecoveryExecutionOptions.QueryExecution()).SearchAsync(CrashFixtureValues.Principal,
            new(EventProjectionCrashScenario.Partition, EventProjectionCrashScenario.Collection,
                VectorField: EventProjectionCrashScenario.VectorField, Vector: [1, 0],
                Space: EventProjectionCrashScenario.Space, Limit: ResultLimit, FusionConstant: FusionConstant),
            cancellationToken);
        var ids = actual.Select(item => item.Document.Reference.Id).ToArray();
        string[] expectedIds = committed
            ? [EventProjectionCrashScenario.TargetId, EventProjectionCrashScenario.BaselineId]
            : [EventProjectionCrashScenario.BaselineId];
        await Assert.That(ids.AsSpan().SequenceEqual(expectedIds)).IsTrue();
        double[] expectedScores = committed ? [1d / 61d, 1d / 62d] : [1d / 61d];
        await Assert.That(actual.Select(item => item.Score).ToArray().AsSpan()
            .SequenceEqual(expectedScores)).IsTrue();
    }

    internal static async Task AssertChangedPayloadConflictsAsync(ZoneTreeStore store, DatabaseEngine database,
        ReplicatedOperation operation, CommitReceipt receipt)
    {
        var command = JsonDefaults.Deserialize<CommandRequest>(Encoding.UTF8.GetBytes(operation.PayloadJson));
        var projection = (ApplyVectorProjection)command.Mutations.Single();
        var changed = projection with { Target = projection.Target with { Values = [0, 1] } };
        var changedCommand = command with { Mutations = [changed] };
        var conflict = database.Apply(operation with
        {
            PayloadJson = JsonSerializer.Serialize(changedCommand, JsonDefaults.Options)
        });
        await Assert.That(conflict.Error).IsEqualTo(ErrorCode.Conflict);
        var durable = OutcomeStoreOracle.Read(database.Store, operation)?.Get<CommitReceipt>();
        await Assert.That(durable is not null && JsonDefaults.Serialize(durable).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        var state = EventProjectionRecoveryStoreOracle.CaptureCommittedBytes(store, operation, projection);
        await AssertVectorBytesAsync(state.Vector, [1, 0]);
    }

    internal static async Task ChangeSourceAndVerifyStaleAsync(string root, ZoneTreeStore store,
        DatabaseEngine database, CommitReceipt receipt, CancellationToken cancellationToken)
    {
        var commandId = Guid.Parse(EventProjectionCrashScenario.SourceChangeCommandId);
        var command = new CommandRequest(commandId, EventProjectionCrashScenario.Partition,
        [new PatchDocument(EventProjectionCrashScenario.Collection, EventProjectionCrashScenario.SourceId,
            [new(EventProjectionCrashScenario.InputField, PatchKind.Set, InputReplacementJson)],
            EventProjectionCrashScenario.DocumentRevision)]);
        var operation = NativeSerialization.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, EventProjectionCrashScenario.OperationFile), cancellationToken));
        var projection = EventProjectionRecoveryStoreOracle.ReadProjection(operation);
        var before = EventProjectionRecoveryStoreOracle.CaptureCommittedBytes(store, operation, projection);
        var baselineHead = NativeSerialization.Deserialize<OutboxHead>(await File.ReadAllBytesAsync(
            Path.Combine(root, EventProjectionCrashScenario.OutboxHeadFile), cancellationToken));
        var projectionOutbox = store.Read(view => view.ReadOwnedValue(
            EventProjectionCrashScenario.OutboxEntryKey(baselineHead.Tail + 1)))
            ?? throw new InvalidOperationException("The committed projection outbox entry was missing.");
        _ = database.Apply(CrashDatabase.Operation(OperationKind.Batch, command, commandId)).Get<CommitReceipt>();
        var updated = store.Read(view => view.GetRecord<DocumentRecord>(EventProjectionCrashScenario.DocumentKey(
            EventProjectionCrashScenario.SourceId)));
        await Assert.That(updated?.Revision).IsEqualTo(EventProjectionCrashScenario.DocumentRevision + 1);
        await Assert.That(updated?.Json).IsEqualTo(EventProjectionCrashScenario.UpdatedSourceDocumentJson);
        await AssertSearchAsync(database, committed: false, cancellationToken);
        var retained = EventProjectionRecoveryStoreOracle.CaptureCommittedBytes(store, operation, projection);
        await AssertVectorBytesAsync(retained.Vector, [1, 0]);
        await Assert.That(retained.Lineage.AsSpan().SequenceEqual(before.Lineage)).IsTrue();
        await Assert.That(retained.Effect.AsSpan().SequenceEqual(before.Effect)).IsTrue();
        await Assert.That(retained.Outcome.AsSpan().SequenceEqual(before.Outcome)).IsTrue();
        var retainedOutbox = store.Read(view => view.ReadOwnedValue(
            EventProjectionCrashScenario.OutboxEntryKey(baselineHead.Tail + 1)));
        await Assert.That(retainedOutbox is not null && retainedOutbox.AsSpan().SequenceEqual(projectionOutbox)).IsTrue();
        var targetBefore = await File.ReadAllBytesAsync(Path.Combine(root,
            EventProjectionCrashScenario.TargetDocumentFile), cancellationToken);
        var targetAfter = store.Read(view => view.ReadOwnedValue(
            EventProjectionCrashScenario.DocumentKey(EventProjectionCrashScenario.TargetId)));
        await Assert.That(targetAfter is not null && targetAfter.AsSpan().SequenceEqual(targetBefore)).IsTrue();
        var replayReceipt = OutcomeStoreOracle.ReadPartition(store, EventProjectionCrashScenario.Partition,
            CrashFixtureValues.Principal, Guid.Parse(EventProjectionCrashScenario.ProjectionCommandId))?.Get<CommitReceipt>();
        await Assert.That(replayReceipt?.Token).IsEqualTo(receipt.Token);
        await EventProjectionRecoveryStoreOracle.AssertSeedBytesAsync(root, store, includeDocuments: false,
            cancellationToken);
    }

    private static async Task AssertVectorBytesAsync(byte[] actual, float[] values)
    {
        var expected = NativeSerialization.Serialize(new VectorRecord(EventProjectionCrashScenario.TargetId,
            EventProjectionCrashScenario.VectorField, EventProjectionCrashScenario.Space, [.. values],
            EventProjectionCrashScenario.DocumentRevision));
        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
    }
}
