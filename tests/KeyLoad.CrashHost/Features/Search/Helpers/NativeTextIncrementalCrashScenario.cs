using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextIncrementalCrashScenario
{
    private const int ArgumentCount = 4;
    private const int RootArgument = 0;
    private const int IncarnationArgument = 1;
    private const int StageArgument = 2;
    private const int ModeArgument = 3;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length != ArgumentCount || args[ModeArgument] is not
            (NativeTextIncrementalCrashProtocol.Prepare or NativeTextIncrementalCrashProtocol.Fault
            or NativeTextIncrementalCrashProtocol.Recover or NativeTextIncrementalCrashProtocol.Verify))
        { return false; }
        var stage = Enum.Parse<NativeTextFaultStage>(args[StageArgument], ignoreCase: false);
        if (stage is not (NativeTextFaultStage.NativePostingWritten
            or NativeTextFaultStage.NativeInventoryFlushed or NativeTextFaultStage.ManifestPublished))
        { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
        await RunAsync(args[RootArgument], Guid.Parse(args[IncarnationArgument]), stage, args[ModeArgument]);
        return true;
    }

    private static async Task RunAsync(string root, Guid incarnation, NativeTextFaultStage stage, string mode)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var boundary = new NativeTextCrashBoundary(stage)
            { Armed = mode == NativeTextIncrementalCrashProtocol.Fault };
            await using var runtime = new NativeTextIncrementalCrashRuntime(root, incarnation, boundary.Observe);
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(root, runtime, mode), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(string root, NativeTextIncrementalCrashRuntime actual, string mode)
    {
        await actual.ResumeAppliedAsync(CancellationToken.None);
        if (mode == NativeTextIncrementalCrashProtocol.Prepare)
        {
            var original = await NativeTextIncrementalCrashSeed.PrepareAsync(actual, CancellationToken.None);
            await NativeTextIncrementalEvidenceFiles.WriteAsync(root, NativeTextIncrementalCrashProtocol.OriginalFile,
                original, CancellationToken.None);
            await CrashHostPause.WaitForKillAsync();
            return;
        }
        var retained = await NativeTextIncrementalEvidenceFiles.ReadAsync<NativeTextIncrementalCrashOriginal>(root,
            mode == NativeTextIncrementalCrashProtocol.Verify ? NativeTextIncrementalCrashProtocol.RequestFile
                : NativeTextIncrementalCrashProtocol.OriginalFile, CancellationToken.None);
        var complete = await NativeTextIncrementalCrashReplay.FinishAsync(actual, retained.Request,
            CancellationToken.None);
        if (mode == NativeTextIncrementalCrashProtocol.Fault)
        { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
        await NativeTextIncrementalCrashCapture.WriteAsync(root, mode == NativeTextIncrementalCrashProtocol.Verify
            ? NativeTextIncrementalCrashProtocol.VerifiedFile : NativeTextIncrementalCrashProtocol.RecoveredFile,
            actual, retained, complete);
        if (mode == NativeTextIncrementalCrashProtocol.Recover)
        { await NativeTextIncrementalCrashCapture.HealthyAsync(root, actual, retained); }
    }
}
