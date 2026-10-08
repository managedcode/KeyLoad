using System.Diagnostics;
using System.Globalization;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Search;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class NativeAnnProcessRecovery
{
    private const int MutationIndex = 0;
    private const string DotnetHost = "dotnet";
    private const string FailedChild = "The native ANN process child exited unsuccessfully.";
    private const int SuccessExit = 0;

    internal static async Task RunAsync(string root, CancellationToken callerToken)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), TimeProvider.System);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(callerToken, limit.Token);
        CommandIdempotencyProcessChild? active = null;
        Exception? primary = null;
        try
        {
            foreach (var mode in NativeAnnCrashContract.Modes)
            {
                var killed = mode == NativeAnnCrashContract.Prepare || mode == NativeAnnCrashContract.Fault;
                var marker = mode == NativeAnnCrashContract.Prepare
                    ? CrashFixtureValues.Acknowledgement : CrashFixtureValues.CrashMarker;
                active = killed ? CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters, marker)
                    : CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters, expectAcknowledgement: false);
                active.Start(CreateStart(root, mode));
                active.ThrowStartupFailure();
                await SettleAsync(active, root, killed, run.Token);
                active = null;
                await NativeAnnProcessAssertions.VerifyStageAsync(root, mode, run.Token);
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
            CommitStage.JournalFlushed.ToString(), MutationIndex.ToString(CultureInfo.InvariantCulture), mode })
        { start.ArgumentList.Add(argument); }
        return start;
    }
}
