using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class ScalarIndexCrashScenario
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary, string mode)
    {
        var database = mode == ScalarIndexCrashContract.PrepareMode
            ? CrashDatabase.Create(store)
            : OpenExisting(store);
        switch (mode)
        {
            case ScalarIndexCrashContract.PrepareMode:
                Prepare(database);
                await ScalarIndexCrashOperations.WriteSnapshotAsync(directory, ScalarIndexCrashContract.PreparedFile, database);
                await CrashHostPause.WaitForKillAsync();
                break;
            case ScalarIndexCrashContract.FaultMode:
                await ScalarIndexCrashOperations.WriteSnapshotAsync(directory, ScalarIndexCrashContract.BeforeCutFile, database);
                await ScalarIndexCrashOperations.ApplyInflightCutAsync(database, store, boundary);
                break;
            case ScalarIndexCrashContract.RecoverMode:
                await ScalarIndexCrashOperations.WriteSnapshotAsync(directory, ScalarIndexCrashContract.RecoveredFile, database);
                ScalarIndexCrashOperations.ApplyHealthyFollowUp(database);
                await ScalarIndexCrashOperations.WriteSnapshotAsync(directory, ScalarIndexCrashContract.HealthyFile, database);
                break;
            case ScalarIndexCrashContract.VerifyMode:
                await ScalarIndexCrashOperations.WriteSnapshotAsync(directory, ScalarIndexCrashContract.FinalFile, database);
                break;
            default:
                throw new InvalidOperationException(ScalarIndexCrashContract.UnsupportedModeMessage);
        }
    }

    private static DatabaseEngine OpenExisting(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(),
            CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(),
            CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(),
            CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(),
            CrashExecutionOptions.TimeSeriesExecution());

    private static void Prepare(DatabaseEngine database)
    {
        ScalarIndexCrashOperations.Configure(database);
        ScalarIndexCrashOperations.Seed(database);
        ScalarIndexCrashOperations.ApplyAcknowledgedMutations(database);
        ScalarIndexCrashOperations.RequireUniqueConflictHasNoEffects(database);
        ScalarIndexCrashOperations.InsertEqualValueInOtherPartition(database);
    }
}
