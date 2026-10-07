using System.Diagnostics;
using System.Globalization;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal static class SampleRollupProcessRecovery
{
    private const string DotnetHost = "dotnet";
    private const string FailedChild = "The native rollup process child exited unsuccessfully.";
    private const int SuccessExit = 0;

    internal static async Task RunAsync(string root, bool drop, CancellationToken callerToken)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), TimeProvider.System);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(callerToken, limit.Token);
        CommandIdempotencyProcessChild? active = null;
        Exception? primary = null;
        try
        {
            foreach (var mode in SampleRollupCrashContract.Modes(drop))
            {
                var killed = mode == SampleRollupCrashContract.PrepareMode
                    || mode == SampleRollupCrashContract.RefreshFaultMode || mode == SampleRollupCrashContract.DropFaultMode;
                var marker = mode == SampleRollupCrashContract.PrepareMode
                    ? CrashFixtureValues.Acknowledgement : CrashFixtureValues.CrashMarker;
                active = killed ? CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters, marker)
                    : CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters, expectAcknowledgement: false);
                active.Start(CreateStart(root, mode));
                active.ThrowStartupFailure();
                await SettleAsync(active, root, killed, run.Token);
                active = null;
                await SampleRollupProcessAssertions.VerifyStageAsync(root, mode, drop, run.Token);
            }
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        { primary = failure; }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        { primary = failure; }
        await CommandIdempotencyProcessCleanup.CleanupActiveTrialAsync(root, active, primary);
    }

    private static async Task SettleAsync(CommandIdempotencyProcessChild child, string root, bool killed, CancellationToken token)
    {
        if (killed)
        {
            await child.WaitForAcknowledgementAsync(token);
            await child.StopAndJoinAsync(token);
        }
        else
        {
            await child.WaitAndJoinAsync(token);
            if (child.ExitCode != SuccessExit)
            { throw new InvalidOperationException(FailedChild); }
        }
        await KilledProcessFileReadiness.WaitAsync(root, token);
        RecoveryFileInventory.AssertNativeHandlesReleased(root);
        child.CloseNativeProcessAfterJoin();
    }

    private static ProcessStartInfo CreateStart(string root, string mode)
    {
        var start = new ProcessStartInfo(DotnetHost)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { typeof(CrashHostMarker).Assembly.Location, root,
            CommitStage.JournalFlushed.ToString(), SampleRollupCrashContract.MutationIndex.ToString(CultureInfo.InvariantCulture), mode })
        { start.ArgumentList.Add(argument); }
        return start;
    }
}
