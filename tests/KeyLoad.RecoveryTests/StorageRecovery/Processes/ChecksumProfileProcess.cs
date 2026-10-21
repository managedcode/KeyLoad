using System.Diagnostics;
using KeyLoad.RecoveryTests.Features.DocumentStorage;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class ChecksumProfileProcess
{
    private const string Prefix = "keyload-execution-checksum-profile-";
    private const string GuidFormat = "N";
    private const string Dotnet = "dotnet";
    private const string Failed = "The current execution checksum profile child failed.";

    internal static async Task RunAsync(CancellationToken callerToken)
    {
        var root = Path.Combine(AppContext.BaseDirectory, Prefix + Guid.NewGuid().ToString(GuidFormat));
        using var originalTimeout = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.CommandTimeout, TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken, originalTimeout.Token);
        CommandIdempotencyProcessChild? active = null;
        Exception? primary = null;
        try
        {
            foreach (var phase in new[] { ChecksumProfileProtocol.Seed,
                ChecksumProfileProtocol.FirstAppend, ChecksumProfileProtocol.SecondAppend,
                ChecksumProfileProtocol.Cold })
            {
                active = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
                    ChecksumProfileProtocol.CompletionSignalPrefix + phase);
                active.Start(Start(root, phase));
                active.ThrowStartupFailure();
                await active.WaitAndJoinAsync(timeout.Token);
                if (active.ExitCode != ChecksumProfileProtocol.Success)
                {
                    throw new InvalidOperationException(Failed);
                }
                await KilledProcessFileReadiness.WaitAsync(root, timeout.Token);
                RecoveryFileInventory.AssertHandlesReleased(root);
                active.CloseProcessAfterJoin();
                active = null;
            }
        }
        catch (Exception error) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(error)) { primary = error; }
        catch (Exception error) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(error)) { primary = error; }
        await CommandIdempotencyProcessCleanup.CleanupActiveTrialAsync(root, active, primary);
    }

    private static ProcessStartInfo Start(string root, string phase)
    {
        var start = new ProcessStartInfo(Dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var value in new[] { typeof(CrashScenarioDispatcher).Assembly.Location, CrashFixtureValues.ProcessMode, ChecksumProfileProtocol.Mode, root, phase })
        {
            start.ArgumentList.Add(value);
        }
        return start;
    }
}
