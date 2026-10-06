using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.TestInfrastructure.Processes;

/// <summary>Bounds child output and settles the original process and stream tasks on every failure path.</summary>
internal static partial class LocalRf3OwnedProcessLifetime
{
    private const string LocalRf3OwnedProcessLifetimeMetadataName = "libc";
    private const string LocalRf3OwnedProcessLifetimeLocalRf3OwnedProcessLifetimeMetadataName = "kill";

    private const int SignalTerminate = 15;

    internal static Task<string> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
        => LocalRf3OwnedProcessObservation.ReadBoundedAsync(reader, maximumCharacters);

    internal static Task ObserveAsync(Task exit, Task output, Task error, TimeProvider timeProvider, CancellationToken cancellationToken)
        => LocalRf3OwnedProcessObservation.ObserveAsync(exit, output, error, timeProvider, cancellationToken);

    internal static async Task TerminateAndJoinAsync(Process process, Task exit, Task output, Task error,
        List<Exception> failures, IOptions<TestExecutionOptions> options, TimeProvider timeProvider)
    {
        const string MessageText = "Local RF3 image process and original stream readers did not settle within the cleanup threshold.";

        var policy = options.Value;
        TrySendTerminate(process, failures);
        if (!HasExited(process, failures))
        {
            _ = await WaitThresholdAsync(exit, policy.TerminationGrace, timeProvider).ConfigureAwait(false);
        }
        if (!HasExited(process, failures))
        {
            TryKill(process, failures);
        }

        var joined = Task.WhenAll(exit, output, error);
        if (!await WaitThresholdAsync(joined, policy.ProcessSettlementTimeout, timeProvider).ConfigureAwait(false))
        {
            failures.Add(new TimeoutException(MessageText));
            if (!HasExited(process, failures))
            {
                TryKill(process, failures);
            }
        }

        await CollectFailureAsync(exit, failures).ConfigureAwait(false);
        if (!HasExited(process, failures))
        {
            await WaitForActualExitAsync(process, failures, policy.ProcessExitPollInterval, timeProvider).ConfigureAwait(false);
        }

        if (!output.IsCompleted)
        {
            CloseOwnedStream(process.StandardOutput, failures);
        }

        if (!error.IsCompleted)
        {
            CloseOwnedStream(process.StandardError, failures);
        }

        await CollectFailureAsync(output, failures).ConfigureAwait(false);
        await CollectFailureAsync(error, failures).ConfigureAwait(false);
        await joined.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _ = joined.Exception;
    }

    private static async Task<bool> WaitThresholdAsync(Task original, TimeSpan threshold, TimeProvider timeProvider)
    {
        using var lifetime = new CancellationTokenSource();
        var elapsed = Task.Delay(threshold, timeProvider, lifetime.Token);
        try
        {
            return await Task.WhenAny(original, elapsed).ConfigureAwait(false) == original;
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            await elapsed.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static bool HasExited(Process process, List<Exception> failures)
    {
        var exited = false;
        OwnedProcessFailureObserver.Observe(() => exited = process.HasExited, failures);
        return exited;
    }

    private static async Task WaitForActualExitAsync(Process process, List<Exception> failures, TimeSpan pollInterval, TimeProvider timeProvider)
    {
        const int CompletionCount = 0;

        var observations = new List<Exception>();
        var observationFailureRecorded = false;
        while (true)
        {
            observations.Clear();
            if (HasExited(process, observations))
            { return; }
            if (!observationFailureRecorded && observations.Count != CompletionCount)
            {
                failures.AddRange(observations);
                observationFailureRecorded = true;
            }
            await Task.Delay(pollInterval, timeProvider).ConfigureAwait(false);
        }
    }

    private static void CloseOwnedStream(StreamReader reader, List<Exception> failures)
    {
        OwnedProcessFailureObserver.Observe(reader.Close, failures);
    }

    private static void TrySendTerminate(Process process, List<Exception> failures)
    {
        const int CompletionCount = 0;

        OwnedProcessFailureObserver.Observe(() =>
        {
            if (process.HasExited || OperatingSystem.IsWindows())
            { return; }
            if (SendSignal(process.Id, SignalTerminate) != CompletionCount && !process.HasExited)
            {
                failures.Add(new Win32Exception(Marshal.GetLastPInvokeError()));
            }
        }, failures);
    }

    private static void TryKill(Process process, List<Exception> failures)
    {
        OwnedProcessFailureObserver.Observe(() =>
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); }
        }, failures);
    }

    private static async Task CollectFailureAsync(Task task, List<Exception> failures)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (task.Exception is { } errors)
        {
            foreach (var error in errors.InnerExceptions)
            {
                if (!failures.Any(failure => ReferenceEquals(failure, error)))
                { failures.Add(error); }
            }
        }
        if (task.IsCanceled)
        {
            try
            { await task.ConfigureAwait(false); }
            catch (OperationCanceledException error) { failures.Add(error); }
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(LocalRf3OwnedProcessLifetimeMetadataName, EntryPoint = LocalRf3OwnedProcessLifetimeLocalRf3OwnedProcessLifetimeMetadataName, SetLastError = true)]
    private static partial int SendSignal(int processId, int signal);
}
