using System.Diagnostics;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class TextIncrementalProcessTrial
{
    private const string Prefix = "keyload-text-incremental-process-";
    private const string Dotnet = "dotnet";
    private const string RetainedRoot = "KeyLoad.TextIncremental.RetainedRoot";
    private const string FailedChild = "The execution incremental text child exited unsuccessfully.";
    private const string Unsettled = "The original execution incremental text child or readers did not settle.";
    private const int SuccessExit = 0;
    private static readonly Guid Incarnation = new("bc1cb10f-c51f-4429-99d1-adb5b1b786cc");
    private static readonly string[] Modes = [TextIncrementalCrashProtocol.Prepare,
        TextIncrementalCrashProtocol.Fault, TextIncrementalCrashProtocol.Recover,
        TextIncrementalCrashProtocol.Verify];

    internal static async Task RunAsync(TextFaultStage stage, CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.CommandTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeout.Token);
        CommandIdempotencyProcessChild? active = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            foreach (var mode in Modes)
            {
                var killed = mode is TextIncrementalCrashProtocol.Prepare or TextIncrementalCrashProtocol.Fault;
                active = CreateChild(mode);
                active.Start(CreateStart(root, stage, mode));
                active.ThrowStartupFailure();
                await CompleteChildAsync(active, killed, linked.Token);
                await KilledProcessFileReadiness.WaitAsync(Path.Combine(root, PartitionStoreProtocol.CanonicalDirectory), linked.Token);
                RecoveryFileInventory.AssertHandlesReleased(root);
                active.CloseProcessAfterJoin();
                active = null;
                await TextIncrementalProcessAssertions.VerifyAsync(root, mode, linked.Token);
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
            TextIncrementalCrashProtocol.Prepare => CrashFixtureValues.Acknowledgement,
            TextIncrementalCrashProtocol.Fault => CrashFixtureValues.CrashMarker,
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
        {
            throw new InvalidOperationException(FailedChild);
        }
    }

    private static ProcessStartInfo CreateStart(string root, TextFaultStage stage, string mode)
    {
        var start = new ProcessStartInfo(Dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { typeof(CrashScenarioDispatcher).Assembly.Location, CrashFixtureValues.ProcessMode, root,
            Incarnation.ToString("D"), stage.ToString(), mode })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }

    private static async Task CleanupAsync(string root, CommandIdempotencyProcessChild? active, Exception? primary)
    {
        using var cleanup = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.CleanupTimeout, TimeProvider.System);
        var failures = new List<Exception>();
        if (active is not null)
        {
            await CommandIdempotencyProcess.SettleActiveAsync(active, failures, cleanup.Token);
        }
        if (active is { IsSettled: false })
        {
            failures.Add(new IOException(Unsettled));
        }
        if (primary is null && failures.Count == SuccessExit)
        {
            await ServerFailureObserver.ObserveAsync(
                () => KilledProcessFileReadiness.WaitAsync(Path.Combine(root, PartitionStoreProtocol.CanonicalDirectory), cleanup.Token), failures);
            if (failures.Count == SuccessExit)
            {
                ServerFailureObserver.Observe(() =>
                {
                    cleanup.Token.ThrowIfCancellationRequested();
                    RecoveryFileInventory.AssertHandlesReleased(root);
                    Directory.Delete(root, recursive: true);
                }, failures);
            }
        }
        primary?.Data[RetainedRoot] = root;
        foreach (var error in failures)
        {
            error.Data[RetainedRoot] = root;
        }
        CommandIdempotencyProcess.ThrowFailures(primary, failures);
    }
}
