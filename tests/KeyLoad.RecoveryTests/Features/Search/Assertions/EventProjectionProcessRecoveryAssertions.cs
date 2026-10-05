using KeyLoad.Core;
using KeyLoad.CrashHost.Features.Search;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class EventProjectionProcessRecoveryAssertions
{
    internal static async Task RecoverAndVerifyAsync(string root, CommitStage stage,
        CancellationToken cancellationToken)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource());
        var operation = NativeSerialization.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, EventProjectionCrashScenario.OperationFile), cancellationToken));
        var projection = EventProjectionRecoveryStoreOracle.ReadProjection(operation);
        var before = await EventProjectionRecoveryStoreOracle.ReadBeforeOutboxHeadAsync(root, cancellationToken);
        var beforeHead = before.Head;
        var beforePositionBytes = await File.ReadAllBytesAsync(Path.Combine(root,
            EventProjectionCrashScenario.PositionFile), cancellationToken);
        var beforePosition = NativeSerialization.Deserialize<long>(beforePositionBytes);
        var cut = EventProjectionRecoveryStoreOracle.CaptureCut(store, operation, projection, beforeHead.Tail);
        var committed = cut.Vector is not null;
        await EventProjectionRecoveryStoreOracle.AssertCutAsync(cut, committed, beforeHead, before.Bytes,
            beforePosition, store.Position, stage);
        await EventProjectionRecoveryStoreOracle.AssertSeedBytesAsync(root, store, includeDocuments: true,
            cancellationToken);
        await EventProjectionRecoveryQueryOracle.AssertSearchAsync(database, committed, cancellationToken);

        var recoveredReceipt = OutcomeStoreOracle.Read(database.Store, operation)?.Get<CommitReceipt>();
        await Assert.That(recoveredReceipt is not null).IsEqualTo(committed);
        var firstReceipt = database.Apply(operation).Get<CommitReceipt>();
        if (recoveredReceipt is not null)
        {
            await Assert.That(JsonDefaults.Serialize(firstReceipt).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize(recoveredReceipt))).IsTrue();
            await Assert.That(firstReceipt.Token).IsEqualTo(recoveredReceipt.Token);
        }
        await EventProjectionRecoveryStoreOracle.AssertReceiptAndOutboxAsync(store, database, operation,
            firstReceipt, beforeHead.Tail);
        await EventProjectionRecoveryStoreOracle.AssertRetryStableAsync(root, store, database, operation,
            firstReceipt, cancellationToken);
        await EventProjectionRecoveryQueryOracle.AssertChangedPayloadConflictsAsync(store, database,
            operation, firstReceipt);
        await EventProjectionRecoveryQueryOracle.ChangeSourceAndVerifyStaleAsync(root, store, database,
            firstReceipt, cancellationToken);
    }
}
