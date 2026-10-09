using System.Globalization;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class SampleChunkCrashScenario
{
    private const int Arguments = 4;
    private const int DirectoryArgument = 0;
    private const int StageArgument = 1;
    private const int MutationArgument = 2;
    private const int ModeArgument = 3;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == Arguments && SampleChunkEarlyCrashProtocol.IsMode(args[ModeArgument]))
        { await SampleChunkEarlyCrashScenario.RunAsync(args).ConfigureAwait(false); return true; }
        if (args.Length != Arguments || !SampleChunkCrashContract.IsMode(args[ModeArgument])) { return false; }
        _ = SerializationExecutionRegistration.Process.Value;
        var mode = args[ModeArgument]; var root = args[DirectoryArgument];
        var boundary = new CanonicalCrashBoundary(Enum.Parse<CommitStage>(args[StageArgument]),
            int.Parse(args[MutationArgument], CultureInfo.InvariantCulture), false);
        using var store = new ZoneTreeStore(new(root) { FaultObserver = boundary.Observe },
            CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        var prepare = mode is SampleChunkCrashContract.PrepareSeal or SampleChunkCrashContract.PrepareMerge;
        var database = prepare ? CrashDatabase.Create(store) : OpenExisting(store);
        if (prepare)
        {
            SampleChunkCrashOperations.Seed(database, mode == SampleChunkCrashContract.PrepareMerge);
            await File.WriteAllBytesAsync(Path.Combine(root, SampleChunkCrashContract.OriginalReceiptFile),
                NativeSerialization.Serialize(SampleChunkCrashOperations.OriginalAcknowledged(database)))
                .ConfigureAwait(false);
            await SampleChunkCrashState.WriteAsync(root, SampleChunkCrashContract.PreparedFile, database).ConfigureAwait(false);
            await CrashHostPause.WaitForKillAsync();
        }
        else if (mode is SampleChunkCrashContract.FaultSeal or SampleChunkCrashContract.FaultMerge)
        { await FaultAsync(root, database, boundary, mode == SampleChunkCrashContract.FaultMerge).ConfigureAwait(false); }
        else if (mode == SampleChunkCrashContract.Verify)
        { await SampleChunkCrashRecovery.VerifyAsync(root, database).ConfigureAwait(false); }
        else
        { await SampleChunkCrashRecovery.RecoverAsync(root, database, mode == SampleChunkCrashContract.RecoverMerge).ConfigureAwait(false); }
        return true;
    }

    private static async Task FaultAsync(string root, DatabaseEngine database,
        CanonicalCrashBoundary boundary, bool merge)
    {
        var request = new CommandRequest(SampleChunkCrashContract.InflightId, SampleChunkCrashContract.Partition,
            [SampleChunkCrashOperations.Intended(merge)]);
        var operation = CrashDatabase.Operation(OperationKind.Batch, request, request.CommandId);
        await File.WriteAllBytesAsync(Path.Combine(root, SampleChunkCrashContract.OriginalOperationFile),
            NativeSerialization.Serialize(operation)).ConfigureAwait(false);
        boundary.Position = checked(database.Store.Position + SampleChunkCrashContract.PositionStep);
        boundary.Armed = true;
        database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    internal static DatabaseEngine OpenExisting(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(),
            CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(),
            CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(),
            CrashExecutionOptions.NativeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution(),
            CrashExecutionOptions.MovementCheckpoints(), UnavailablePartitionMovementCheckpointVerifier.Instance);
}
