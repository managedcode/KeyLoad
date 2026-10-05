using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal static class CommandIdempotencyRecoveryAssertions
{
    internal static async Task AssertRecoveredStoreAsync(string root, CancellationToken cancellationToken)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource());
        // This sidecar is the frozen client request only; recovered state comes solely from ZoneTree.
        var operation = await CommandIdempotencyCrashData.ReadEvidenceAsync<ReplicatedOperation>(root,
            CommandIdempotencyCrashContract.CommandEvidenceFile, cancellationToken);
        var expected = await CommandIdempotencyCrashData.ReadEvidenceAsync<CommitReceipt>(root,
            CommandIdempotencyCrashContract.ReceiptEvidenceFile, cancellationToken);
        var seedTail = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, CommandIdempotencyCrashContract.SeedTailEvidenceFile, cancellationToken);
        var durable = OutcomeStoreOracle.Read(database.Store, operation)?.Get<CommitReceipt>()
            ?? throw new InvalidOperationException("The authorized command outcome was not retained after restart.");
        CommandIdempotencyCrashAssertions.RequireSameReceipt(durable, expected);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(
            CommandIdempotencyCrashAssertions.RequireReceipt(database.ResolveOutcome(operation)), expected);
        CommandIdempotencyCrashAssertions.AssertCanonicalEffects(database, store, expected, seedTail + 4, seedTail);
        CommandIdempotencyCrashAssertions.RequireDocument(database.GetDocument(CrashFixtureValues.Principal,
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.FollowUpDocumentId)),
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.FollowUpDocumentId),
            1, CommandIdempotencyCrashContract.FollowUpJson);
        AssertHealthyFollowUp(database, store, seedTail + 4);
    }

    private static void AssertHealthyFollowUp(DatabaseEngine database, ZoneTreeStore store, long expectedTail)
    {
        var receipt = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            CommandIdempotencyCrashContract.Partition, CrashFixtureValues.Principal, CommandIdempotencyCrashContract.FollowUpCommandId)))?.Result.Get<CommitReceipt>()
            ?? throw new InvalidOperationException("The healthy follow-up outcome was not persisted.");
        CommandIdempotencyCrashAssertions.RequireFollowUp(receipt);
        var tail = database.GetOutboxStatus(CrashFixtureValues.Principal, CommandIdempotencyCrashContract.Partition).Head.Tail;
        if (tail != expectedTail)
        {
            throw new InvalidOperationException("The healthy follow-up changed the outbox by an unexpected number of effects.");
        }
        var entry = store.Read(view => view.GetRecord<OutboxEntry>(KeySpace.Partition(CommandIdempotencyCrashContract.TailKeySpace,
            CommandIdempotencyCrashContract.Partition, expectedTail)));
        if (entry is null || entry.Mutation is not PutDocument mutation || mutation.Id != CommandIdempotencyCrashContract.FollowUpDocumentId
            || !CommandIdempotencyCrashAssertions.SameMutationReceipt(entry.Receipt, receipt.Mutations[0]) || entry.Commit != receipt.Token)
        {
            throw new InvalidOperationException("The healthy follow-up outbox effect did not match its durable receipt.");
        }
    }
}
