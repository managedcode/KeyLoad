using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;
namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class TopicPurgeRecoveryOracle
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken cancellationToken)
    {
        var operation = await ReadAsync<ReplicatedOperation>(root, TopicPurgeCrashContract.OperationFile, cancellationToken);
        var seedPosition = await ReadAsync<long>(root, TopicPurgeCrashContract.PositionFile, cancellationToken);
        var pinned = await ReadAsync<bool>(root, TopicPurgeCrashContract.PinnedFile, cancellationToken);
        var cursor = await ReadAsync<string>(root, TopicPurgeCrashContract.CursorFile, cancellationToken);
        var protectedBytes = await ReadAsync<string[]>(root, TopicPurgeCrashContract.ProtectedFile, cancellationToken);
        string[] finalBytes;
        long finalPosition;
        using (var store = NewStore(root))
        {
            var database = Open(store);
            var recovered = OutcomeStoreOracle.Read(store, operation);
            await Assert.That(store.Position).IsEqualTo(seedPosition + (recovered is null ? 0 : 1));
            if (stage is CommitStage.JournalFlushed or CommitStage.MutationApplied or CommitStage.ApplyCompleted)
            { await Assert.That(recovered).IsNotNull(); }
            await TopicPurgeModelAssertions.VerifyAsync(database, await ReadAsync<ReplicatedOperation>(root,
                TopicPurgeCrashContract.PublishOperationFile, cancellationToken), pinned || recovered is null ? 2 : 3, pinned, cursor);
            await Assert.That(TopicPurgeCrashScenario.ProtectedBytes(store)).IsEquivalentTo(protectedBytes, CollectionOrdering.Matching);
            var outcome = database.Apply(operation);
            await TopicPurgeReceiptAssertions.VerifyAsync(store, operation, outcome, seedPosition + 1, pinned);
            if (recovered is not null)
            { await EventAppendReceiptAssertions.SameResultAsync(recovered, outcome); }
            await TopicPurgeModelAssertions.VerifyAsync(database, await ReadAsync<ReplicatedOperation>(root,
                TopicPurgeCrashContract.PublishOperationFile, cancellationToken), pinned ? 2 : 3, pinned, cursor);
            await Assert.That(TopicPurgeCrashScenario.ProtectedBytes(store)).IsEquivalentTo(protectedBytes, CollectionOrdering.Matching);
            await ReplaySeedAsync(root, database, cancellationToken);
            await TopicPurgeReceiptAssertions.StableReplayAsync(database, operation, outcome);
            await ContentConflictAsync(database, operation, outcome);
            await TopicPurgeContinuation.VerifyAsync(database, operation, pinned);
            finalBytes = EventAppendStateAssertions.Bytes(store);
            finalPosition = store.Position;
        }
        using var finalStore = NewStore(root);
        var finalDatabase = Open(finalStore);
        await Assert.That(finalStore.Position).IsEqualTo(finalPosition);
        await EventAppendStateAssertions.SameBytesAsync(finalBytes, finalStore);
        await TopicPurgeContinuation.FinalAsync(finalDatabase, pinned);
        var settled = EventAppendStateAssertions.Bytes(finalStore);
        var position = finalStore.Position;
        await TopicPurgeReceiptAssertions.StableReplayAsync(finalDatabase, operation, OutcomeStoreOracle.Read(finalStore, operation)!);
        await Assert.That(finalStore.Position).IsEqualTo(position);
        await EventAppendStateAssertions.SameBytesAsync(settled, finalStore);
    }
    private static async Task ContentConflictAsync(DatabaseEngine database, ReplicatedOperation operation, OperationResult outcome)
    {
        var bytes = EventAppendStateAssertions.Bytes(database.Store);
        var position = database.Store.Position;
        var changed = TopicPurgeCrashContract.Purge(operation.Id, 3);
        var result = database.Apply(operation with { PayloadJson = JsonSerializer.Serialize(changed, JsonDefaults.Options) });
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(result.SafeDetail).IsEqualTo("The command ID was already used with different content.");
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await EventAppendStateAssertions.SameBytesAsync(bytes, database.Store);
        await TopicPurgeReceiptAssertions.StableReplayAsync(database, operation, outcome);
    }
    private static async Task ReplaySeedAsync(string root, DatabaseEngine database, CancellationToken token)
    {
        foreach (var pair in new[] { (TopicPurgeCrashContract.SeedOperationFile, TopicPurgeCrashContract.SeedReceiptFile),
            (TopicPurgeCrashContract.PublishOperationFile, TopicPurgeCrashContract.PublishReceiptFile) })
        {
            var original = await ReadAsync<ReplicatedOperation>(root, pair.Item1, token);
            var receipt = await ReadAsync<OperationResult>(root, pair.Item2, token);
            await TopicPurgeReceiptAssertions.StableReplayAsync(database, original, receipt);
        }
    }
    private static Task<T> ReadAsync<T>(string root, string file, CancellationToken token)
        => CommandIdempotencyCrashData.ReadEvidenceAsync<T>(root, file, token);
    private static ZoneTreeStore NewStore(string root) => new(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
    internal static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(),
        RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(),
        RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(),
        RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution(), RecoveryExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
}
