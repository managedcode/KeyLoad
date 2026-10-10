using System.Diagnostics;
using System.Globalization;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal static class SampleChunkProcessRecovery
{
    private const string DotnetHost = "dotnet";
    private const string FailedChild = "The native chunk process child exited unsuccessfully.";
    private const int SuccessExit = 0;

    internal static Task RunAsync(string root, bool merge, CancellationToken callerToken)
        => RunOwnedAsync(root, SampleChunkCrashContract.Modes(merge),
            (mode, token) => SampleChunkProcessAssertions.VerifyAsync(root, mode, merge, token), callerToken);

    internal static Task RunEarlyAsync(string root, SampleChunkEarlyCut cut, CancellationToken callerToken)
        => RunOwnedAsync(root, SampleChunkEarlyCrashProtocol.Modes(cut),
            (mode, token) => SampleChunkEarlyProcessAssertions.VerifyAsync(root, mode, cut, token), callerToken);

    private static async Task RunOwnedAsync(string root, string[] modes,
        Func<string, CancellationToken, Task> verify, CancellationToken callerToken)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), TimeProvider.System);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(callerToken, limit.Token);
        CommandIdempotencyProcessChild? active = null;
        Exception? primary = null;
        try
        {
            foreach (var mode in modes)
            {
                var prepare = mode is SampleChunkCrashContract.PrepareSeal or SampleChunkCrashContract.PrepareMerge
                    || SampleChunkEarlyCrashProtocol.IsPrepare(mode);
                var killed = prepare || mode is SampleChunkCrashContract.FaultSeal or SampleChunkCrashContract.FaultMerge
                    || SampleChunkEarlyCrashProtocol.IsFault(mode);
                var marker = prepare ? CrashFixtureValues.Acknowledgement : CrashFixtureValues.CrashMarker;
                active = killed ? CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters, marker)
                    : CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters, expectAcknowledgement: false);
                active.Start(CreateStart(root, mode));
                active.ThrowStartupFailure();
                await SettleAsync(active, root, killed, run.Token).ConfigureAwait(false);
                active = null;
                await verify(mode, run.Token).ConfigureAwait(false);
            }
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        { primary = failure; }
        catch (Exception fatal)
        {
            await CommandIdempotencyProcessCleanup.CleanupActiveTrialAsync(root, active, fatal).ConfigureAwait(false);
            throw;
        }
        await CommandIdempotencyProcessCleanup.CleanupActiveTrialAsync(root, active, primary).ConfigureAwait(false);
    }

    private static async Task SettleAsync(CommandIdempotencyProcessChild child, string root,
        bool killed, CancellationToken token)
    {
        if (killed)
        { await child.WaitForAcknowledgementAsync(token); await child.StopAndJoinAsync(token); }
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
            CommitStage.JournalFlushed.ToString(), SampleChunkCrashContract.MutationIndex.ToString(CultureInfo.InvariantCulture), mode })
        { start.ArgumentList.Add(argument); }
        return start;
    }
}
