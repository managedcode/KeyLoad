using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class SampleRollupCrashScenario
{
    private const long InitialRevision = 0;

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary, string mode)
    {
        var database = mode == SampleRollupCrashContract.PrepareMode ? CrashDatabase.Create(store) : OpenExisting(store);
        switch (mode)
        {
            case SampleRollupCrashContract.PrepareMode:
                SampleRollupCrashOperations.Seed(database);
                var receipt = SampleRollupCrashOperations.Commit(database, SampleRollupCrashContract.AcknowledgedId,
                    SampleRollupCrashContract.Refresh(InitialRevision)).Get<CommitReceipt>();
                await File.WriteAllBytesAsync(Path.Combine(directory, SampleRollupCrashContract.AcknowledgedReceiptFile), NativeSerialization.Serialize(receipt));
                await File.WriteAllBytesAsync(Path.Combine(directory, SampleRollupCrashContract.RawFile), SampleRollupCrashState.Raw(database));
                await SampleRollupCrashState.WriteAsync(directory, SampleRollupCrashContract.PreparedFile, database);
                await CrashHostPause.WaitForKillAsync();
                break;
            case SampleRollupCrashContract.RefreshFaultMode:
            case SampleRollupCrashContract.DropFaultMode:
                await FaultAsync(directory, database, boundary, mode == SampleRollupCrashContract.DropFaultMode);
                break;
            case SampleRollupCrashContract.RefreshRecoverMode:
            case SampleRollupCrashContract.DropRecoverMode:
                await SampleRollupCrashState.WriteAsync(directory, SampleRollupCrashContract.RecoveredFile, database);
                SampleRollupCrashReplay.Verify(directory, database, mode == SampleRollupCrashContract.DropRecoverMode);
                await SampleRollupCrashOperations.HealthyAsync(directory, database);
                await SampleRollupCrashState.WriteAsync(directory, SampleRollupCrashContract.HealthyFile, database);
                break;
            case SampleRollupCrashContract.VerifyMode:
                await SampleRollupCrashState.WriteAsync(directory, SampleRollupCrashContract.FinalFile, database);
                break;
            default:
                throw new InvalidOperationException(SampleRollupCrashContract.Invalid);
        }
    }

    private static async Task FaultAsync(string directory, DatabaseEngine database, CanonicalCrashBoundary boundary, bool drop)
    {
        await SampleRollupCrashState.WriteAsync(directory, SampleRollupCrashContract.BeforeFile, database);
        var request = new CommandRequest(SampleRollupCrashContract.InflightId, SampleRollupCrashContract.Partition,
            [SampleRollupCrashOperations.Inflight(drop)]);
        var operation = CrashDatabase.Operation(OperationKind.Batch, request, SampleRollupCrashContract.InflightId);
        await File.WriteAllBytesAsync(Path.Combine(directory, SampleRollupCrashContract.OperationFile), NativeSerialization.Serialize(operation));
        boundary.Position = checked(database.Store.Position + SampleRollupCrashContract.PositionStep);
        boundary.Armed = true;
        database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    private static DatabaseEngine OpenExisting(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(),
            CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(),
            CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(),
            CrashExecutionOptions.NativeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution());
}
