using System.ComponentModel;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyLoad.AppHost.Features.TestInfrastructure.Processes;

/// <summary>Bounds child output and settles the original process and stream tasks on every failure path.</summary>
internal static partial class LocalRf3OwnedProcessLifetime
{
    private const int SignalTerminate = 15;

    internal static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
    {
        var buffer = new char[maximumCharacters + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(count)).ConfigureAwait(false);
            if (read == 0)
            {
                return new string(buffer, 0, count);
            }

            count += read;
        }
        throw new InvalidOperationException("Local RF3 image cleanup output exceeded its bound.");
    }

    internal static async Task ObserveAsync(Task exit, Task output, Task error, CancellationToken cancellationToken)
    {
        var canceled = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var observed = new HashSet<Task>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ObserveCompletedAsync(exit, observed).ConfigureAwait(false);
            await ObserveCompletedAsync(output, observed).ConfigureAwait(false);
            await ObserveCompletedAsync(error, observed).ConfigureAwait(false);
            if (observed.Count == 3)
            {
                return;
            }

            var pending = new List<Task>(4) { canceled };
            if (!observed.Contains(exit))
            {
                pending.Add(exit);
            }

            if (!observed.Contains(output))
            {
                pending.Add(output);
            }

            if (!observed.Contains(error))
            {
                pending.Add(error);
            }

            var completed = await Task.WhenAny(pending).ConfigureAwait(false);
            if (completed == canceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            await completed.ConfigureAwait(false);
        }
    }

    internal static async Task TerminateAndJoinAsync(Process process, Task exit, Task output, Task error,
        List<Exception> failures, IOptions<TestExecutionOptions> options)
    {
        var policy = options.Value;
        TrySendTerminate(process, failures);
        if (!HasExited(process, failures))
        {
            await Task.WhenAny(exit, Task.Delay(policy.TerminationGrace)).ConfigureAwait(false);
        }
        if (!HasExited(process, failures))
        {
            TryKill(process, failures);
        }

        var joined = Task.WhenAll(exit, output, error);
        if (await Task.WhenAny(joined, Task.Delay(policy.ProcessSettlementTimeout)).ConfigureAwait(false) != joined)
        {
            failures.Add(new TimeoutException("Local RF3 image process and original stream readers did not settle within the cleanup threshold."));
            if (!HasExited(process, failures))
            {
                TryKill(process, failures);
            }
        }

        await CollectFailureAsync(exit, failures).ConfigureAwait(false);
        if (!HasExited(process, failures))
        {
            await WaitForActualExitAsync(process, failures, policy.ProcessExitPollInterval).ConfigureAwait(false);
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

    private static async Task ObserveCompletedAsync(Task original, HashSet<Task> observed)
    {
        if (!original.IsCompleted)
        {
            return;
        }

        await original.ConfigureAwait(false);
        observed.Add(original);
    }

    private static bool HasExited(Process process, List<Exception> failures)
    {
        var exited = false;
        OwnedProcessFailureObserver.Observe(() => exited = process.HasExited, failures);
        return exited;
    }

    private static async Task WaitForActualExitAsync(Process process, List<Exception> failures, TimeSpan pollInterval)
    {
        var observations = new List<Exception>();
        var observationFailureRecorded = false;
        while (true)
        {
            observations.Clear();
            if (HasExited(process, observations))
            { return; }
            if (!observationFailureRecorded && observations.Count != 0)
            {
                failures.AddRange(observations);
                observationFailureRecorded = true;
            }
            await Task.Delay(pollInterval).ConfigureAwait(false);
        }
    }

    private static void CloseOwnedStream(StreamReader reader, List<Exception> failures)
    {
        OwnedProcessFailureObserver.Observe(reader.Close, failures);
    }

    private static void TrySendTerminate(Process process, List<Exception> failures)
    {
        OwnedProcessFailureObserver.Observe(() =>
        {
            if (process.HasExited || OperatingSystem.IsWindows())
            { return; }
            if (SendSignal(process.Id, SignalTerminate) != 0 && !process.HasExited)
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
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int SendSignal(int processId, int signal);
}
