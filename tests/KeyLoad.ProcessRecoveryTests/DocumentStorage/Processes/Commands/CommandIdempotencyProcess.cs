using System.Diagnostics;
using System.Globalization;
using KeyLoad.ProcessRecoveryTests.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.DocumentStorage;

internal static class CommandIdempotencyProcess
{
    internal const int OutputLimitCharacters = 8_192;
    private const int MutationIndex = 0;

    internal static async Task RunAsync(string root, CancellationToken callerToken)
    {
        using var timeoutTimeout = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.CommandTimeout, TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutTimeout.Token);
        CommandIdempotencyProcessChild? active = null;
        Exception? primary = null;
        try
        {
            active = CommandIdempotencyProcessChild.Create(OutputLimitCharacters, expectAcknowledgement: true);
            Start(active, root, CommandIdempotencyCrashContract.FirstMode);
            active.ThrowStartupFailure();
            await active.WaitForAcknowledgementAsync(timeout.Token);
            await active.StopAndJoinAsync(timeout.Token);
            await KilledProcessFileReadiness.WaitAsync(root, timeout.Token);
            RecoveryFileInventory.AssertHandlesReleased(root);
            active.CloseProcessAfterJoin();
            active = null;

            active = CommandIdempotencyProcessChild.Create(OutputLimitCharacters, expectAcknowledgement: false);
            Start(active, root, CommandIdempotencyCrashContract.ReplayMode);
            active.ThrowStartupFailure();
            await active.WaitAndJoinAsync(timeout.Token);
            if (active.ExitCode != 0)
            {
                throw new InvalidOperationException("The post-restart command verification process failed.");
            }
            await KilledProcessFileReadiness.WaitAsync(root, timeout.Token);
            RecoveryFileInventory.AssertHandlesReleased(root);
            await CommandIdempotencyRecoveryAssertions.AssertRecoveredStoreAsync(root, timeout.Token);
            active.CloseProcessAfterJoin();
            active = null;
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

    private static void Start(CommandIdempotencyProcessChild child, string root, string mode)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
        {
            typeof(CrashScenarioDispatcher).Assembly.Location, CrashFixtureValues.ProcessMode,
            root,
            CommitStage.ApplyCompleted.ToString(),
            MutationIndex.ToString(CultureInfo.InvariantCulture),
            mode
        })
        {
            start.ArgumentList.Add(argument);
        }
        child.Start(start);
    }

    internal static async Task SettleActiveAsync(CommandIdempotencyProcessChild active, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        if (!active.IsSettled)
        {
            try
            {
                await active.StopAndJoinAsync(cancellationToken);
            }
            catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
            {
                CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
            }
            catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
            {
                CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
            }
        }
        if (active.IsSettled && !active.IsDisposed)
        {
            try
            {
                active.CloseProcessAfterJoin();
            }
            catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
            {
                CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
            }
            catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
            {
                CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
            }
        }
    }

    internal static async Task CleanTrialAsync(string root, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            await RecoveryProcessCleanup.SettleAsync(null, root, root, null, cancellationToken);
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
        }
    }

    internal static void ThrowFailures(Exception? primary, List<Exception> cleanupFailures)
    {
        var failures = new List<Exception>(cleanupFailures.Count + (primary is null ? 0 : 1));
        if (primary is not null)
        {
            failures.Add(primary);
        }
        foreach (var failure in cleanupFailures)
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, failure);
        }
        CommandIdempotencyProcessFailureHandling.ThrowFailures(failures);
    }
}
