using System.Globalization;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class SampleChunkEarlyCrashScenario
{
    internal static async Task RunAsync(string[] args)
    {
        var mode = args[SampleChunkEarlyCrashProtocol.ModeArgument];
        var root = args[SampleChunkEarlyCrashProtocol.RootArgument];
        var cut = SampleChunkEarlyCrashProtocol.Cut(mode);
        _ = SerializationExecutionRegistration.Process.Value;
        var boundary = new CanonicalCrashBoundary(Enum.Parse<CommitStage>(args[SampleChunkEarlyCrashProtocol.StageArgument]),
            int.Parse(args[SampleChunkEarlyCrashProtocol.MutationArgument], CultureInfo.InvariantCulture), false);
        using var store = new ZoneTreeStore(new(root) { FaultObserver = boundary.Observe },
            CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        var database = SampleChunkEarlyCrashProtocol.IsPrepare(mode)
            ? CrashDatabase.Create(store) : SampleChunkCrashScenario.OpenExisting(store);
        if (SampleChunkEarlyCrashProtocol.IsPrepare(mode))
        {
            SampleChunkEarlyCrashOperations.Seed(database, cut);
            await File.WriteAllBytesAsync(Path.Combine(root, SampleChunkCrashContract.OriginalReceiptFile),
                NativeSerialization.Serialize(SampleChunkEarlyCrashOperations.Configure(database))).ConfigureAwait(false);
            await SampleChunkEarlyCrashState.WriteAsync(root, SampleChunkCrashContract.PreparedFile, database).ConfigureAwait(false);
            await CrashHostPause.WaitForKillAsync();
        }
        else if (SampleChunkEarlyCrashProtocol.IsFault(mode))
        { await FaultAsync(root, database, boundary, cut).ConfigureAwait(false); }
        else
        { await RecoverAsync(root, database, cut, SampleChunkEarlyCrashProtocol.IsVerify(mode)).ConfigureAwait(false); }
    }

    private static async Task FaultAsync(string root, DatabaseEngine database, CanonicalCrashBoundary boundary,
        SampleChunkEarlyCut cut)
    {
        var request = new CommandRequest(SampleChunkCrashContract.InflightId, SampleChunkCrashContract.Partition,
            [SampleChunkEarlyCrashOperations.Intended(cut)]);
        var operation = CrashDatabase.Operation(OperationKind.Batch, request, request.CommandId);
        await File.WriteAllBytesAsync(Path.Combine(root, SampleChunkCrashContract.OriginalOperationFile),
            NativeSerialization.Serialize(operation)).ConfigureAwait(false);
        boundary.Position = checked(database.Store.Position + SampleChunkCrashContract.PositionStep);
        boundary.Armed = true;
        database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    private static async Task RecoverAsync(string root, DatabaseEngine database, SampleChunkEarlyCut cut, bool verify)
    {
        var result = await SampleChunkEarlyCrashState.ReplayAsync(root, database).ConfigureAwait(false);
        if (verify)
        {
            await SampleChunkEarlyCrashState.WriteAsync(root, SampleChunkCrashContract.FinalFile, database, result)
                .ConfigureAwait(false);
            return;
        }
        await SampleChunkEarlyCrashState.WriteAsync(root, SampleChunkCrashContract.RecoveredFile, database, result)
            .ConfigureAwait(false);
        SampleChunkEarlyCrashOperations.Complete(database, cut);
        await SampleChunkEarlyCrashState.WriteAsync(root, SampleChunkCrashContract.HealthyFile, database, result)
            .ConfigureAwait(false);
    }
}
