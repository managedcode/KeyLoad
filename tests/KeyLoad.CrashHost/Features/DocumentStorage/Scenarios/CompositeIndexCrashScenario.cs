using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class CompositeIndexCrashScenario
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary, string mode)
    {
        var database = mode == CompositeIndexCrashContract.PrepareMode
            ? CrashDatabase.Create(store)
            : OpenExisting(store);
        switch (mode)
        {
            case CompositeIndexCrashContract.PrepareMode:
                await PrepareAsync(directory, database);
                await CompositeIndexCrashOperations.WriteSnapshotAsync(directory, CompositeIndexCrashContract.PreparedFile, database);
                await CrashHostPause.WaitForKillAsync();
                break;
            case CompositeIndexCrashContract.FaultMode:
                await CompositeIndexCrashOperations.WriteSnapshotAsync(directory, CompositeIndexCrashContract.BeforeCutFile, database);
                await CompositeIndexCrashOperations.ApplyInflightCutAsync(database, store, boundary);
                break;
            case CompositeIndexCrashContract.RecoverMode:
                await CompositeIndexCrashOperations.WriteSnapshotAsync(directory, CompositeIndexCrashContract.RecoveredFile, database);
                CompositeIndexReplayAssertions.Verify(directory, database);
                CompositeIndexCrashOperations.ApplyHealthyFollowUp(database);
                await CompositeIndexCrashOperations.WriteSnapshotAsync(directory, CompositeIndexCrashContract.HealthyFile, database);
                break;
            case CompositeIndexCrashContract.VerifyMode:
                await CompositeIndexCrashOperations.WriteSnapshotAsync(directory, CompositeIndexCrashContract.FinalFile, database);
                break;
            default:
                throw new InvalidOperationException(CompositeIndexCrashContract.UnsupportedModeMessage);
        }
    }

    private static DatabaseEngine OpenExisting(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(),
            CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(),
            CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(),
            CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(),
            CrashExecutionOptions.TimeSeriesExecution());

    private static async Task PrepareAsync(string directory, DatabaseEngine database)
    {
        CompositeIndexCrashOperations.Configure(database);
        CompositeIndexCrashOperations.Seed(database);
        await CompositeIndexCrashOperations.WriteSnapshotAsync(directory, CompositeIndexCrashContract.SeededFile, database);
        CompositeIndexCrashOperations.ApplyAcknowledgedMutations(database);
        CompositeIndexReplayAssertions.Prepare(directory, database);
        CompositeIndexCrashOperations.InsertEqualValueInOtherPartition(database);
    }
}
