using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class TextIncrementalCrashScenario
{
    private const int ArgumentCount = 4;
    private const int RootArgument = 0;
    private const int IncarnationArgument = 1;
    private const int StageArgument = 2;
    private const int ModeArgument = 3;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length != ArgumentCount || args[ModeArgument] is not
            (TextIncrementalCrashProtocol.Prepare or TextIncrementalCrashProtocol.Fault
            or TextIncrementalCrashProtocol.Recover or TextIncrementalCrashProtocol.Verify))
        {
            return false;
        }
        var stage = Enum.Parse<TextFaultStage>(args[StageArgument], ignoreCase: false);
        if (stage is not (TextFaultStage.PostingWritten
            or TextFaultStage.InventoryFlushed or TextFaultStage.ManifestPublished
            or TextFaultStage.IncrementalIntentFlushed or TextFaultStage.DeletionWritten
            or TextFaultStage.AdditionWritten or TextFaultStage.CanonicalCheckpointAcknowledged
            or TextFaultStage.PendingIntentRetired))
        {
            throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        }
        await RunAsync(args[RootArgument], Guid.Parse(args[IncarnationArgument]), stage, args[ModeArgument]);
        return true;
    }

    private static async Task RunAsync(string root, Guid incarnation, TextFaultStage stage, string mode)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var boundary = new TextCrashBoundary(stage)
            {
                Armed = mode == TextIncrementalCrashProtocol.Fault
            };
            await using var runtime = new TextIncrementalCrashRuntime(root, incarnation, boundary.Observe);
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(root, runtime, mode), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(string root, TextIncrementalCrashRuntime actual, string mode)
    {
        await actual.ResumeAppliedAsync(CancellationToken.None);
        if (mode == TextIncrementalCrashProtocol.Prepare)
        {
            var original = await TextIncrementalCrashSeed.PrepareAsync(actual, CancellationToken.None);
            await TextIncrementalEvidenceFiles.WriteAsync(root, TextIncrementalCrashProtocol.OriginalFile,
                original, CancellationToken.None);
            await RecoveryProcessPause.WaitForKillAsync();
            return;
        }
        var retained = await TextIncrementalEvidenceFiles.ReadAsync<TextIncrementalCrashOriginal>(root,
            mode == TextIncrementalCrashProtocol.Verify ? TextIncrementalCrashProtocol.RequestFile
                : TextIncrementalCrashProtocol.OriginalFile, CancellationToken.None);
        var complete = await TextIncrementalCrashReplay.FinishAsync(actual, retained.Request,
            CancellationToken.None);
        if (mode == TextIncrementalCrashProtocol.Fault)
        {
            throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        }
        await TextIncrementalCrashCapture.WriteAsync(root, mode == TextIncrementalCrashProtocol.Verify
            ? TextIncrementalCrashProtocol.VerifiedFile : TextIncrementalCrashProtocol.RecoveredFile,
            actual, retained, complete);
        if (mode == TextIncrementalCrashProtocol.Recover)
        {
            await TextIncrementalCrashCapture.HealthyAsync(root, actual, retained);
        }
    }
}
