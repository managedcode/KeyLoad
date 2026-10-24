using KeyLoad.Server;
using KeyLoad.Server.Search;

namespace KeyLoad.RecoveryTests.Search;

internal static class TextOnlineCrashScenario
{
    private const int ExpectedArgumentCount = 4;
    private const int RootArgument = 0;
    private const int IncarnationArgument = 1;
    private const int StageArgument = 2;
    private const int ModeArgument = 3;
    internal const string OriginalAlias = "keyload.crashhost.text-online-original";
    internal const string Prepare = "execution-text-online-prepare";
    internal const string Fault = "execution-text-online-fault";
    internal const string Corrupt = "execution-text-online-corrupt";
    internal const string PublicationLeafFile = "text-online-publication-leaf.bin";
    internal const string PublicationBytesFile = "text-online-publication-bytes.bin";
    internal const string Recover = "execution-text-online-recover";
    internal const string Verify = "execution-text-online-verify";
    internal const string OriginalFile = "text-online-original.bin";
    internal const string ReceiptFile = "text-online-receipt.bin";
    internal const string HealthyRequestFile = "text-online-healthy-request.bin";
    internal const string HealthyReceiptFile = "text-online-healthy-receipt.bin";

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length != ExpectedArgumentCount || args[ModeArgument] is not (Prepare or Fault or Corrupt or Recover or Verify))
        {
            return false;
        }
        var stage = Enum.Parse<TextFaultStage>(args[StageArgument], ignoreCase: false);
        if (stage is not (TextFaultStage.CanonicalOnlinePublicationAcknowledged
            or TextFaultStage.OnlineCatalogPendingFlushed))
        {
            throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        }
        if (args[ModeArgument] == Corrupt)
        {
            await RequireColdCorruptionAsync(args[RootArgument], Guid.Parse(args[IncarnationArgument]));
            return true;
        }
        var boundary = new TextCrashBoundary(stage) { Armed = args[ModeArgument] == Fault };
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var runtime = new TextOnlineCrashRuntime(args[RootArgument], Guid.Parse(args[IncarnationArgument]), boundary.Observe);
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(args[RootArgument], args[ModeArgument], runtime), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return true;
    }

    private static async Task RunOwnedAsync(string root, string mode, TextOnlineCrashRuntime runtime)
    {
        await runtime.Canonical.ResumeAppliedAsync(CancellationToken.None);
        if (mode == Prepare)
        {
            var seeded = await TextIncrementalCrashSeed.PrepareAsync(runtime.Canonical, CancellationToken.None);
            var original = new TextOnlineCrashOriginal(new(Guid.NewGuid(), seeded.Request.Consumer,
                seeded.Request.Collection, seeded.Request.Field, seeded.Request.IndexGeneration,
                seeded.Request.NodeId, seeded.Request.Placement), seeded.Receipt, seeded.Mutation);
            await TextIncrementalEvidenceFiles.WriteAsync(root, OriginalFile, original, CancellationToken.None);
            await RecoveryProcessPause.WaitForKillAsync();
            return;
        }
        var retained = await TextIncrementalEvidenceFiles.ReadAsync<TextOnlineCrashOriginal>(root,
            OriginalFile, CancellationToken.None);
        if (mode == Fault)
        {
            _ = await TextOnlineCrashFlow.RunAsync(runtime, retained.Request, CancellationToken.None,
                result => TextOnlineCrashProof.CapturePublicationAsync(root, runtime, retained.Request, result));
            throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        }
        await TextOnlineCrashProof.RecoverAsync(root, runtime, retained, mode == Verify);
    }
    private static async Task RequireColdCorruptionAsync(string root, Guid incarnation)
    {
        var refused = false;
        try
        {
            await using var runtime = new TextOnlineCrashRuntime(root, incarnation, null);
            await runtime.Canonical.ResumeAppliedAsync(CancellationToken.None);
            var original = await TextIncrementalEvidenceFiles.ReadAsync<TextOnlineCrashOriginal>(root,
                OriginalFile, CancellationToken.None);
            _ = await TextOnlineCrashFlow.RunAsync(runtime, original.Request, CancellationToken.None);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            refused = true;
        }
        if (!refused)
        {
            throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        }
        await RecoveryProcessPause.WriteAcknowledgementAsync();
    }

}
