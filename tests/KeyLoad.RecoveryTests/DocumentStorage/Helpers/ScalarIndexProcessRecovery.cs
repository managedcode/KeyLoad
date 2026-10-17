using System.Diagnostics;
using System.Globalization;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal static class ScalarIndexProcessRecovery
{
    private const int CutMutationIndex = 0;
    private const string DotnetHost = "dotnet";
    private const string UnsuccessfulExitMessage = "The scalar-index recovery CrashHost exited unsuccessfully.";

    internal static async Task RunAsync(string root, CancellationToken callerToken)
    {
        using var runLimit = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), TimeProvider.System);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(callerToken, runLimit.Token);
        CommandIdempotencyProcessChild? active = null;
        Exception? primary = null;
        try
        {
            var prepareStart = CreateStart(root, ScalarIndexCrashContract.PrepareMode);
            active = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
                CrashFixtureValues.Acknowledgement);
            Start(active, prepareStart);
            await active.WaitForAcknowledgementAsync(run.Token);
            await KillAndReleaseAsync(active, root, run.Token);
            active = null;
            await ScalarIndexReferenceModelAssertions.AssertFileAsync(root, ScalarIndexCrashContract.PreparedFile,
                ScalarIndexReferenceModelAssertions.Prepared());

            var faultStart = CreateStart(root, ScalarIndexCrashContract.FaultMode);
            active = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
                CrashFixtureValues.CrashMarker);
            Start(active, faultStart);
            await active.WaitForAcknowledgementAsync(run.Token);
            await KillAndReleaseAsync(active, root, run.Token);
            active = null;
            await ScalarIndexReferenceModelAssertions.AssertFileAsync(root, ScalarIndexCrashContract.BeforeCutFile,
                ScalarIndexReferenceModelAssertions.Prepared());

            var recoverStart = CreateStart(root, ScalarIndexCrashContract.RecoverMode);
            active = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
                expectAcknowledgement: false);
            Start(active, recoverStart);
            await active.WaitAndJoinAsync(run.Token);
            RequireSuccessfulExit(active);
            await ReleaseAfterExitAsync(active, root, run.Token);
            active = null;
            await ScalarIndexReferenceModelAssertions.AssertFileAsync(root, ScalarIndexCrashContract.RecoveredFile,
                ScalarIndexReferenceModelAssertions.Recovered());
            await ScalarIndexReferenceModelAssertions.AssertFileAsync(root, ScalarIndexCrashContract.HealthyFile,
                ScalarIndexReferenceModelAssertions.Healthy());

            var verifyStart = CreateStart(root, ScalarIndexCrashContract.VerifyMode);
            active = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
                expectAcknowledgement: false);
            Start(active, verifyStart);
            await active.WaitAndJoinAsync(run.Token);
            RequireSuccessfulExit(active);
            await ReleaseAfterExitAsync(active, root, run.Token);
            active = null;
            await ScalarIndexReferenceModelAssertions.AssertFileAsync(root, ScalarIndexCrashContract.FinalFile,
                ScalarIndexReferenceModelAssertions.Healthy());
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            primary = failure;
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            primary = failure;
        }
        await CommandIdempotencyProcessCleanup.CleanupActiveTrialAsync(root, active, primary);
    }

    private static ProcessStartInfo CreateStart(string root, string mode)
    {
        var start = new ProcessStartInfo(DotnetHost)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
        {
            typeof(CrashHostMarker).Assembly.Location, root, CommitStage.JournalFlushed.ToString(),
            CutMutationIndex.ToString(CultureInfo.InvariantCulture), mode
        })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }

    private static void Start(CommandIdempotencyProcessChild child, ProcessStartInfo start)
    {
        child.Start(start);
        child.ThrowStartupFailure();
    }

    private static async Task KillAndReleaseAsync(CommandIdempotencyProcessChild child, string root,
        CancellationToken cancellationToken)
    {
        await child.StopAndJoinAsync(cancellationToken);
        await KilledProcessFileReadiness.WaitAsync(root, cancellationToken);
        RecoveryFileInventory.AssertNativeHandlesReleased(root);
        child.CloseNativeProcessAfterJoin();
    }

    private static async Task ReleaseAfterExitAsync(CommandIdempotencyProcessChild child, string root,
        CancellationToken cancellationToken)
    {
        await KilledProcessFileReadiness.WaitAsync(root, cancellationToken);
        RecoveryFileInventory.AssertNativeHandlesReleased(root);
        child.CloseNativeProcessAfterJoin();
    }

    private static void RequireSuccessfulExit(CommandIdempotencyProcessChild child)
    {
        if (child.ExitCode != 0)
        {
            throw new InvalidOperationException(UnsuccessfulExitMessage);
        }
    }
}
