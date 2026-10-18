using ManagedCode.Communication.Commands;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.RecoveryHost;
using KeyLoad.RecoveryHost.Features.DocumentStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class EventAppendRecoveryOracle
{
    private const long PositionStep = 1;
    private const string ContentConflict = "The command ID was already used with different content.";

    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken cancellationToken)
    {
        var original = await CommandIdempotencyCrashData.ReadEvidenceAsync<Command<DatabaseCommand>>(root,
            EventAppendCrashContract.OperationFile, cancellationToken);
        var seedPosition = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root,
            EventAppendCrashContract.SeedPositionFile, cancellationToken);
        var seedTail = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root,
            EventAppendCrashContract.SeedTailFile, cancellationToken);
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(),
            RecoveryExecutionOptions.PointCacheExecution());
        var database = Open(store);
        var recovered = OutcomeStoreOracle.Read(store, original);
        var committed = recovered is not null;
        await Assert.That(store.Position).IsEqualTo(seedPosition + (committed ? PositionStep : 0));
        if (stage is CommitStage.JournalFlushed or CommitStage.MutationApplied or CommitStage.ApplyCompleted)
        {
            await Assert.That(committed).IsTrue();
        }
        await EventAppendStateAssertions.VerifyAsync(database, new DateTimeOffset(original.Timestamp), seedTail, committed, healthy: false);
        if (recovered is not null)
        {
            await EventAppendReceiptAssertions.VerifyAsync(store, recovered.Value.Get<CommitReceipt>(), seedPosition + PositionStep);
        }
        await EventAppendReceiptAssertions.AssertOutboxPresenceAsync(store, seedTail, committed);
        var applied = database.Apply(original);
        await EventAppendReceiptAssertions.VerifyAsync(store, applied.Get<CommitReceipt>(), seedPosition + PositionStep);
        await EventAppendStateAssertions.VerifyAsync(database, new DateTimeOffset(original.Timestamp), seedTail, committed: true, healthy: false);
        if (recovered is not null)
        {
            await EventAppendReceiptAssertions.SameResultAsync(recovered, applied);
        }
        await EventAppendReceiptAssertions.VerifyOutboxAsync(store, applied.Get<CommitReceipt>(), seedTail, new DateTimeOffset(original.Timestamp));
        var stablePosition = store.Position;
        var stable = EventAppendStateAssertions.Bytes(store);
        await EventAppendReceiptAssertions.SameResultAsync(applied, database.Apply(original));
        await Assert.That(store.Position).IsEqualTo(stablePosition);
        await EventAppendStateAssertions.SameBytesAsync(stable, store);
        var changedRequest = EventAppendCrashContract.ProducerCommand(original.CommandId, EventAppendCrashContract.Producer, 1)
            with
        {
            Mutations = [new PutDocument(EventAppendCrashContract.Documents, EventAppendCrashContract.Producer,
                EventAppendCrashContract.ChangedJson, 0)]
        };
        var conflict = database.Apply(database.CreateCommand(original.Value!.Kind, original.CommandId, original.UserId!, new DateTimeOffset(original.Timestamp), JsonSerializer.Serialize(changedRequest, JsonDefaults.Options)));
        await Assert.That(Errors.Code(conflict.Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(conflict.Problem?.Detail).IsEqualTo(ContentConflict);
        await Assert.That(store.Position).IsEqualTo(stablePosition);
        await EventAppendStateAssertions.SameBytesAsync(stable, store);
        await EventAppendReceiptAssertions.SameResultAsync(applied, database.Apply(original));
        await EventAppendDedupAssertions.VerifyAsync(database, new DateTimeOffset(original.Timestamp), seedTail);
        var healthy = EventAppendCrashContract.ProducerCommand(EventAppendCrashContract.HealthyCommandId,
            EventAppendCrashContract.Healthy, 2);
        var result = database.Apply(database.CreateCommand(original.Value!.Kind, healthy.CommandId, original.UserId!, new DateTimeOffset(original.Timestamp), JsonSerializer.Serialize(healthy, JsonDefaults.Options)));
        await EventAppendReceiptAssertions.VerifyHealthyAsync(store, result.Get<CommitReceipt>());
        await EventAppendReceiptAssertions.VerifyOutboxAsync(store, result.Get<CommitReceipt>(), seedTail + 3, new DateTimeOffset(original.Timestamp));
        await EventAppendStateAssertions.VerifyAsync(database, new DateTimeOffset(original.Timestamp), seedTail, committed: true, healthy: true);
    }

    private static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(),
        RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(),
        RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(),
        RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
}
