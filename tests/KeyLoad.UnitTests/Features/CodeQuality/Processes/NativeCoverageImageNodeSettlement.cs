using System.Diagnostics;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed record NativeCoverageImageNodeJoined(int ExitCode, string StandardOutput,
    string StandardError, bool ExitJoined, bool OutputJoined, bool ErrorJoined)
{
    internal static NativeCoverageImageNodeJoined Empty { get; } = new(-1, string.Empty, string.Empty,
        false, false, false);
}

internal static class NativeCoverageImageNodeSettlement
{
    internal static async Task<NativeCoverageImageNodeJoined> SettleAsync(Process process, bool processStarted,
        Task original, Task? exit, Task<string>? output, Task<string>? error, TimeSpan settlementTimeout,
        TimeProvider timeProvider, TimeSpan terminationGrace, TimeSpan processExitPollInterval,
        List<Exception> failures)
    {
        await using var deadline = new NativeProcessCleanupDeadline(settlementTimeout, timeProvider);
        if (NeedsCleanup(original, failures) && processStarted)
        {
            ServerFailureObserver.Observe(() => TryStop(process), failures);
        }
        if (!await deadline.TryJoinAsync(original, terminationGrace).ConfigureAwait(false))
        {
            if (processStarted)
            {
                CloseOriginalReaders(process, failures);
            }
            if (!await deadline.TryJoinAsync(original).ConfigureAwait(false))
            {
                NativeProcessCleanupDeadline.FailStop();
            }
        }
        if (processStarted)
        {
            await deadline.ConfirmNativeExitAsync(process, processExitPollInterval).ConfigureAwait(false);
        }
        var stdout = await ObserveTextAsync(output, failures).ConfigureAwait(false);
        var stderr = await ObserveTextAsync(error, failures).ConfigureAwait(false);
        var exitJoined = exit?.IsCompleted == true;
        var exitCode = -1;
        if (exitJoined)
        {
            ServerFailureObserver.Observe(() => exitCode = process.HasExited ? process.ExitCode : -1, failures);
        }
        return new(exitCode, stdout.Text, stderr.Text, exitJoined, stdout.Joined, stderr.Joined);
    }

    internal static void ThrowFailures(List<Exception> failures)
        => ServerFailureObserver.ThrowIfAny(failures);

    private static bool NeedsCleanup(Task original, List<Exception> failures)
        => failures.Count != 0 || !original.IsCompleted || !original.IsCompletedSuccessfully;

    private static async Task<(string Text, bool Joined)> ObserveTextAsync(Task<string>? original,
        List<Exception> failures)
    {
        if (original is null || !original.IsCompleted)
        {
            return (string.Empty, false);
        }
        await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
        return (original.IsCompletedSuccessfully ? await original.ConfigureAwait(false) : string.Empty, true);
    }

    private static void TryStop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
    }

    private static void CloseOriginalReaders(Process process, List<Exception> failures)
    {
        ServerFailureObserver.Observe(process.StandardOutput.Dispose, failures);
        ServerFailureObserver.Observe(process.StandardError.Dispose, failures);
    }
}
