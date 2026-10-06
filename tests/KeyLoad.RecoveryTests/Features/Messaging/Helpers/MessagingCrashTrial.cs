using System.Diagnostics;
using System.Globalization;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class MessagingCrashTrial
{
    private const int TrialTimeoutSeconds = 90;
    private const int CleanupTimeoutSeconds = 30;
    private const int MaximumErrorCharacters = 8_192;
    private const string MarkerFailure = "The messaging crash process missed its requested commit boundary: ";

    internal static async Task RunAsync(string prefix, string mode, CommitStage stage, int mutationIndex,
        Func<string, CommitStage, CancellationToken, Task> verify, CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Process? process = null;
        using var timeoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(TrialTimeoutSeconds), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutTimeout.Token);
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            process = Start(root, mode, stage, mutationIndex);
            await AwaitMarkerAsync(process, timeout.Token);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
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
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds), TimeProvider.System);
            await EpochUpgradeCleanup.SettleAsync(process, root, root, activeFailure, cleanup.Token);
        }
    }

    private static Process Start(string root, string mode, CommitStage stage, int mutationIndex)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
        {
            typeof(CrashHostMarker).Assembly.Location,
            root,
            stage.ToString(),
            mutationIndex.ToString(CultureInfo.InvariantCulture),
            mode
        })
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the messaging CrashHost.");
    }

    private static async Task AwaitMarkerAsync(Process process, CancellationToken cancellationToken)
    {
        var marker = await process.StandardOutput.ReadLineAsync(cancellationToken);
        if (string.Equals(marker, CrashFixtureValues.CrashMarker, StringComparison.Ordinal))
        {
            return;
        }
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
        }
        var error = await ReadBoundedErrorAsync(process.StandardError, cancellationToken);
        throw new InvalidOperationException(MarkerFailure + error);
    }

    private static async Task<string> ReadBoundedErrorAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[MaximumErrorCharacters];
        var count = 0;
        int read;
        while (count < buffer.Length
            && (read = await reader.ReadAsync(buffer.AsMemory(count), cancellationToken)) != 0)
        {
            count += read;
        }
        return new string(buffer, 0, count);
    }
}
