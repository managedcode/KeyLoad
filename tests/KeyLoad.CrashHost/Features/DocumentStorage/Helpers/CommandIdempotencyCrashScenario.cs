using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class CommandIdempotencyCrashScenario
{
    internal static async Task RunFirstAsync(string directory, ZoneTreeStore store)
    {
        const int InitialOutboxTail = 3;

        var database = CrashDatabase.Create(store);
        CommandIdempotencyCrashData.ConfigureResources(database);
        var seedTail = database.GetOutboxStatus(CrashFixtureValues.Principal, CommandIdempotencyCrashContract.Partition).Head.Tail;
        var operation = CommandIdempotencyCrashData.CreateOperation();
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, CommandIdempotencyCrashContract.CommandEvidenceFile, operation);
        var receipt = CommandIdempotencyCrashAssertions.RequireReceipt(database.Apply(operation));
        CommandIdempotencyCrashAssertions.RequireInitialReceipt(receipt);
        await SaveEvidenceAsync(directory, receipt, seedTail);
        CommandIdempotencyCrashAssertions.AssertCanonicalEffects(database, store, receipt, seedTail + InitialOutboxTail, seedTail);
        CommandIdempotencyCrashAssertions.AssertRetriesAndConflict(database, store, operation, receipt, seedTail + InitialOutboxTail, seedTail);
        await CrashHostPause.WaitForKillAsync();
    }

    internal static async Task RunReplayAsync(string directory, ZoneTreeStore store)
    {
        const string RunReplayAsyncMessageText = "The retained command outcome was missing after process restart.";
        const int InitialOutboxTail = 3;
        const int FollowUpOutboxTail = 4;
        const int InitialRevision = 1;

        var database = new DatabaseEngine(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution(), CrashExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        // Evidence supplies the original caller request only; recovered state is read exclusively from ZoneTree.
        var operation = await CommandIdempotencyCrashData.ReadEvidenceAsync<ReplicatedOperation>(directory,
            CommandIdempotencyCrashContract.CommandEvidenceFile);
        var expected = await CommandIdempotencyCrashData.ReadEvidenceAsync<CommitReceipt>(directory,
            CommandIdempotencyCrashContract.ReceiptEvidenceFile);
        var seedTail = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(directory,
            CommandIdempotencyCrashContract.SeedTailEvidenceFile);
        var recovered = database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            CommandIdempotencyCrashContract.Partition, CrashFixtureValues.Principal, operation.Id)))?.Result.Get<CommitReceipt>()
            ?? throw new InvalidOperationException(RunReplayAsyncMessageText);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(recovered, expected);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(
            CommandIdempotencyCrashAssertions.RequireReceipt(database.ResolveOutcome(operation)), expected);
        CommandIdempotencyCrashAssertions.AssertCanonicalEffects(database, store, expected, seedTail + InitialOutboxTail, seedTail);
        CommandIdempotencyCrashAssertions.AssertRetriesAndConflict(database, store, operation, expected, seedTail + InitialOutboxTail, seedTail);
        var followUp = CommandIdempotencyCrashData.CreateFollowUp(database);
        CommandIdempotencyCrashAssertions.RequireFollowUp(followUp);
        CommandIdempotencyCrashAssertions.AssertCanonicalEffects(database, store, expected, seedTail + FollowUpOutboxTail, seedTail);
        CommandIdempotencyCrashAssertions.RequireDocument(database.GetDocument(CrashFixtureValues.Principal,
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.FollowUpDocumentId)),
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.FollowUpDocumentId),
            InitialRevision, CommandIdempotencyCrashContract.FollowUpJson);
    }

    private static async Task SaveEvidenceAsync(string directory, CommitReceipt receipt, long seedTail)
    {
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, CommandIdempotencyCrashContract.ReceiptEvidenceFile, receipt);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, CommandIdempotencyCrashContract.SeedTailEvidenceFile, seedTail);
    }
}
