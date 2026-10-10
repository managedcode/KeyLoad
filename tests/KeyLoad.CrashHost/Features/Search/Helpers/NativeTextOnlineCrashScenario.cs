using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextOnlineCrashScenario
{
    private const int ExpectedArgumentCount = 4;
    private const int RootArgument = 0;
    private const int IncarnationArgument = 1;
    private const int StageArgument = 2;
    private const int ModeArgument = 3;
    internal const string OriginalAlias = "keyload.crashhost.text-online-original.v1";
    internal const string Prepare = "native-text-online-prepare";
    internal const string Fault = "native-text-online-fault";
    internal const string Recover = "native-text-online-recover";
    internal const string Verify = "native-text-online-verify";
    internal const string OriginalFile = "text-online-original.bin";
    internal const string ReceiptFile = "text-online-receipt.bin";
    internal const string HealthyRequestFile = "text-online-healthy-request.bin";
    internal const string HealthyReceiptFile = "text-online-healthy-receipt.bin";

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length != ExpectedArgumentCount || args[ModeArgument] is not (Prepare or Fault or Recover or Verify))
        { return false; }
        var stage = Enum.Parse<NativeTextFaultStage>(args[StageArgument], ignoreCase: false);
        if (stage is not (NativeTextFaultStage.CanonicalOnlinePublicationAcknowledged
            or NativeTextFaultStage.OnlineCatalogPendingFlushed))
        { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
        var boundary = new NativeTextCrashBoundary(stage) { Armed = args[ModeArgument] == Fault };
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var runtime = new NativeTextOnlineCrashRuntime(args[RootArgument], Guid.Parse(args[IncarnationArgument]), boundary.Observe);
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(args[RootArgument], args[ModeArgument], runtime), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return true;
    }

    private static async Task RunOwnedAsync(string root, string mode, NativeTextOnlineCrashRuntime runtime)
    {
        await runtime.Canonical.ResumeAppliedAsync(CancellationToken.None);
        if (mode == Prepare)
        {
            var seeded = await NativeTextIncrementalCrashSeed.PrepareAsync(runtime.Canonical, CancellationToken.None);
            var original = new NativeTextOnlineCrashOriginal(new(Guid.NewGuid(), seeded.Request.Consumer,
                seeded.Request.Collection, seeded.Request.Field, seeded.Request.IndexGeneration,
                seeded.Request.NodeId, seeded.Request.Placement), seeded.Receipt, seeded.Mutation);
            await NativeTextIncrementalEvidenceFiles.WriteAsync(root, OriginalFile, original, CancellationToken.None);
            await CrashHostPause.WaitForKillAsync();
            return;
        }
        var retained = await NativeTextIncrementalEvidenceFiles.ReadAsync<NativeTextOnlineCrashOriginal>(root,
            OriginalFile, CancellationToken.None);
        if (mode == Fault)
        {
            _ = await NativeTextOnlineCrashFlow.RunAsync(runtime, retained.Request, CancellationToken.None,
                result => NativeTextIncrementalEvidenceFiles.WriteAsync(root, ReceiptFile, result, CancellationToken.None));
            throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
        }
        await NativeTextOnlineCrashProof.RecoverAsync(root, runtime, retained, mode == Verify);
    }
}
