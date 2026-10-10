using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Search;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class NativeTextOnlineProcessTrial
{
    private const string Prefix = "keyload-text-online-process-";
    private const string Dotnet = "dotnet";
    private const string RetainedRoot = "KeyLoad.NativeTextOnline.RetainedRoot";
    private const string FailedChild = "The native incremental text child exited unsuccessfully.";
    private const string Unsettled = "The original native incremental text child or readers did not settle.";
    private const int SuccessExit = 0;
    private static readonly Guid Incarnation = new("bc1cb10f-c51f-4429-99d1-adb5b1b786cc");
    private static readonly string[] Modes = [NativeTextOnlineCrashScenario.Prepare,
        NativeTextOnlineCrashScenario.Fault, NativeTextOnlineCrashScenario.Recover,
        NativeTextOnlineCrashScenario.Verify];

    internal static async Task RunAsync(NativeTextFaultStage stage, CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeout.Token);
        CommandIdempotencyProcessChild? active = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            foreach (var mode in Modes)
            {
                var killed = mode is NativeTextOnlineCrashScenario.Prepare or NativeTextOnlineCrashScenario.Fault;
                active = CreateChild(mode);
                active.Start(CreateStart(root, stage, mode));
                active.ThrowStartupFailure();
                await CompleteChildAsync(active, killed, linked.Token);
                await ReplicaProcessFiles.WaitForOwnershipAsync(root, linked.Token);
                RecoveryFileInventory.AssertNativeHandlesReleased(root);
                active.CloseNativeProcessAfterJoin();
                active = null;
                await NativeTextOnlineProcessAssertions.VerifyAsync(root, mode, linked.Token);
            }
        }
        catch (Exception error) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(error)) { primary = error; }
        catch (Exception error) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(error)) { primary = error; }
        await CleanupAsync(root, active, primary);
    }

    private static CommandIdempotencyProcessChild CreateChild(string mode)
    {
        var marker = mode switch
        {
            NativeTextOnlineCrashScenario.Prepare => CrashFixtureValues.Acknowledgement,
            NativeTextOnlineCrashScenario.Fault => CrashFixtureValues.CrashMarker,
            _ => null
        };
        return CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters, marker);
    }

    private static async Task CompleteChildAsync(CommandIdempotencyProcessChild active, bool killed, CancellationToken token)
    {
        if (killed)
        {
            await active.WaitForAcknowledgementAsync(token);
            await active.StopAndJoinAsync(token);
            return;
        }
        await active.WaitAndJoinAsync(token);
        if (active.ExitCode != SuccessExit)
        { throw new InvalidOperationException(FailedChild); }
    }

    private static ProcessStartInfo CreateStart(string root, NativeTextFaultStage stage, string mode)
    {
        var start = new ProcessStartInfo(Dotnet)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root,
            Incarnation.ToString("D"), stage.ToString(), mode })
        { start.ArgumentList.Add(argument); }
        return start;
    }

    private static async Task CleanupAsync(string root, CommandIdempotencyProcessChild? active, Exception? primary)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds), TimeProvider.System);
        var failures = new List<Exception>();
        if (active is not null)
        { await CommandIdempotencyProcess.SettleActiveAsync(active, failures, cleanup.Token); }
        if (active is { IsSettled: false })
        { failures.Add(new IOException(Unsettled)); }
        if (primary is null && failures.Count == SuccessExit)
        {
            await ServerFailureObserver.ObserveAsync(
                () => ReplicaProcessFiles.WaitForOwnershipAsync(root, cleanup.Token), failures);
            if (failures.Count == SuccessExit)
            { await ServerFailureObserver.ObserveAsync(() => ReplicaProcessFiles.DeleteAsync(root, cleanup.Token), failures); }
        }
        primary?.Data[RetainedRoot] = root;
        foreach (var error in failures)
        { error.Data[RetainedRoot] = root; }
        CommandIdempotencyProcess.ThrowFailures(primary, failures);
    }
}
