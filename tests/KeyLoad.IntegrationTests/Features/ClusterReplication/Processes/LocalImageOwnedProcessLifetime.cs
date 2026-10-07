using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication.Processes;

/// <summary>Bounds child output and settles the original process and stream tasks on every failure path.</summary>
internal static partial class LocalImageOwnedProcessLifetime
{
    private static readonly TimeSpan TerminationGrace = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SettlementLimit = TimeSpan.FromSeconds(5);
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
        throw new InvalidOperationException("Local RF3 image verifier output exceeded its bound.");
    }

    internal static async Task ObserveAsync(Task exit, Task output, Task error, CancellationToken cancellationToken)
    {
        var canceled = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, cancellationToken);
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

    internal static async Task TerminateAndJoinAsync(Process process, Task? exit, Task? output, Task? error,
        List<Exception> failures)
    {
        var actualExit = exit ?? WaitForActualExitAsync(process, failures);
        TrySendTerminate(process, failures);
        if (!HasExited(process, failures))
        {
            await Task.WhenAny(actualExit, Task.Delay(TerminationGrace, TimeProvider.System)).ConfigureAwait(false);
        }
        if (!HasExited(process, failures))
        {
            TryKill(process, failures);
        }

        var originalTasks = new Task?[] { actualExit, output, error }.OfType<Task>().ToArray();
        var joined = Task.WhenAll(originalTasks);
        if (await Task.WhenAny(joined, Task.Delay(SettlementLimit, TimeProvider.System)).ConfigureAwait(false) != joined)
        {
            failures.Add(new TimeoutException("Local RF3 image process and original stream readers did not settle within the cleanup threshold."));
            if (!HasExited(process, failures))
            {
                TryKill(process, failures);
            }
        }

        await CollectFailureAsync(actualExit, failures).ConfigureAwait(false);
        if (!HasExited(process, failures))
        {
            await WaitForActualExitAsync(process, failures).ConfigureAwait(false);
        }

        if (output is null || !output.IsCompleted)
        {
            CloseOwnedStream(process, standardOutput: true, failures);
        }

        if (error is null || !error.IsCompleted)
        {
            CloseOwnedStream(process, standardOutput: false, failures);
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

    private static async Task WaitForActualExitAsync(Process process, List<Exception> failures)
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
            await Task.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System).ConfigureAwait(false);
        }
    }

    private static void CloseOwnedStream(Process process, bool standardOutput, List<Exception> failures)
    {
        OwnedProcessFailureObserver.Observe(() =>
            (standardOutput ? process.StandardOutput : process.StandardError).Close(), failures);
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

    private static async Task CollectFailureAsync(Task? task, List<Exception> failures)
    {
        if (task is null)
        { return; }
        var captured = await OwnedProcessFailureObserver.CaptureAsync(task).ConfigureAwait(false);
        IEnumerable<Exception> errors = captured is AggregateException aggregate
            ? aggregate.InnerExceptions : captured is null ? [] : new[] { captured };
        foreach (var error in errors)
        {
            if (!failures.Any(failure => ReferenceEquals(failure, error)))
            { failures.Add(error); }
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int SendSignal(int processId, int signal);
}
