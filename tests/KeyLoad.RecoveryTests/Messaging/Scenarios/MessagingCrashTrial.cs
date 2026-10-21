using System.Diagnostics;
using System.Globalization;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class MessagingCrashTrial
{
    private const int MaximumOutputCharacters = 8_192;

    internal static async Task RunAsync(string prefix, string mode, CommitStage stage, int mutationIndex,
        Func<string, CommitStage, CancellationToken, Task> verify, CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(AppContext.BaseDirectory, prefix + Guid.NewGuid().ToString("N"));
        CommandIdempotencyProcessChild? child = null;
        using var timeoutTimeout = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.CommandTimeout, TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutTimeout.Token);
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            child = CommandIdempotencyProcessChild.Create(MaximumOutputCharacters, CrashFixtureValues.CrashMarker);
            child.Start(CreateStart(root, mode, stage, mutationIndex));
            child.ThrowStartupFailure();
            await child.WaitForAcknowledgementAsync(timeout.Token);
            await child.StopAndJoinAsync(timeout.Token);
            await KilledProcessFileReadiness.WaitAsync(root, timeout.Token);
            await verify(root, stage, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            await CommandIdempotencyProcessCleanup.CleanupActiveTrialAsync(root, child, activeFailure);
        }
    }

    private static ProcessStartInfo CreateStart(string root, string mode, CommitStage stage, int mutationIndex)
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
            stage.ToString(),
            mutationIndex.ToString(CultureInfo.InvariantCulture),
            mode
        })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }
}
