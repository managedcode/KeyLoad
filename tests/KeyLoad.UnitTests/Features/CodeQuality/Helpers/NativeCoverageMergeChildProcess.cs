using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.BackupRestore;
using KeyLoad.UnitTests.Features.TestInfrastructure;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageMergeChildProcess
{
    private const string NativeOptionsEnvironment = "KEYLOAD_NATIVE_COVERAGE_OPTIONS_JSON";
    private const string TelemetryVariable = "DOTNET_COVERAGE_TELEMETRY_OPTOUT";
    private const string EnabledValue = "1";
    private const string StartFailure = "The native coverage tooling child process did not start.";
    private const string TimeoutFailure = "The original native coverage tooling child did not settle.";
    private const string FailStopMessage = "The native coverage tooling child or original readers did not settle.";
    private const int FailStopExitCode = 1;
    private const int InfiniteWaitMilliseconds = Timeout.Infinite;

    internal sealed record Result(int ExitCode, string StandardOutput, string StandardError,
        bool ExitJoined, bool OutputJoined, bool ErrorJoined, bool Disposed);

    private sealed record RunAttempt(Result? Result, bool OriginalTasksUnsettled);

    internal static ProcessStartInfo CreateStartInfo(string executable, int maximumOutputCharacters,
        NativeCoverageExecutionOptions coverage)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutputCharacters);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment[TelemetryVariable] = EnabledValue;
        start.Environment[NativeOptionsEnvironment] = JsonSerializer.Serialize(coverage);
        return start;
    }

    internal static async Task<Result> RunAsync(ProcessStartInfo start, TimeSpan timeout,
        TimeSpan settlement, int maximumOutputCharacters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        using var process = new Process { StartInfo = start };
        var failures = new List<Exception>();
        var started = false;
        var unsettled = false;
        var disposed = false;
        Result? result = null;
        var startedAt = new TestElapsedClock(TimeProvider.System);
        try
        {
            ServerFailureObserver.Observe(() => started = process.Start(), failures);
            if (!started && failures.Count == 0)
            { failures.Add(new InvalidOperationException(StartFailure)); }
            if (started)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var attempt = await RunStartedAsync(process, startedAt, timeout, settlement,
                        maximumOutputCharacters, failures, cancellationToken).ConfigureAwait(false);
                    result = attempt.Result;
                    unsettled = attempt.OriginalTasksUnsettled;
                }, failures).ConfigureAwait(false);
            }
        }
        finally
        {
            if (unsettled)
            { FailStop(); }
            ServerFailureObserver.Observe(() =>
            {
                process.Dispose();
                disposed = true;
            }, failures);
        }
        if (result is not null && disposed)
        { result = result with { Disposed = true }; }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(StartFailure);
    }

    private static async Task<RunAttempt> RunStartedAsync(Process process, TestElapsedClock startedAt, TimeSpan timeout,
        TimeSpan settlement, int maximumOutputCharacters, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        using var deadlineTimeout = new CancellationTokenSource(RemainingPrimaryTime(startedAt, timeout), startedAt.Provider);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        var stdout = new CliBackupRestoreProcessOutput(maximumOutputCharacters);
        var stderr = new CliBackupRestoreProcessOutput(maximumOutputCharacters);
        Task? exit = null;
        Task? output = null;
        Task? error = null;
        ServerFailureObserver.Observe(() => exit = process.WaitForExitAsync(), failures);
        ServerFailureObserver.Observe(() => output = stdout.DrainAsync(process.StandardOutput), failures);
        ServerFailureObserver.Observe(() => error = stderr.DrainAsync(process.StandardError), failures);
        var joined = Task.WhenAll(exit ?? Task.CompletedTask, output ?? Task.CompletedTask, error ?? Task.CompletedTask);
        var unsettled = await ObserveUntilDeadlineAsync(process, joined, startedAt, timeout,
            settlement, failures, deadline.Token, cancellationToken).ConfigureAwait(false);
        var result = unsettled ? null : MakeResult(process, stdout, stderr, exit, output, error, joined);
        return new(result, unsettled);
    }

    private static async Task<bool> ObserveUntilDeadlineAsync(Process process, Task joined,
        TestElapsedClock startedAt, TimeSpan timeout, TimeSpan settlement, List<Exception> failures, CancellationToken deadlineToken,
        CancellationToken callerToken)
    {
        var deadlineSignal = Task.Delay(TimeSpan.FromMilliseconds(InfiniteWaitMilliseconds), startedAt.Provider, deadlineToken);
        _ = await Task.WhenAny(joined, deadlineSignal).ConfigureAwait(false);
        if (!joined.IsCompleted)
        {
            failures.Add(callerToken.IsCancellationRequested
                ? new OperationCanceledException(callerToken) : new TimeoutException(TimeoutFailure));
            if (!await SettleOriginalTasksAsync(process, joined, startedAt, timeout, settlement, failures)
                    .ConfigureAwait(false))
            {
                return true;
            }
        }
        await ServerFailureObserver.ObserveAsync(() => joined, failures).ConfigureAwait(false);
        return false;
    }

    private static async Task<bool> SettleOriginalTasksAsync(Process process, Task joined, TestElapsedClock startedAt,
        TimeSpan timeout, TimeSpan settlement, List<Exception> failures)
    {
        using var cleanupDeadline = new CancellationTokenSource(RemainingCleanupTime(startedAt, timeout, settlement), startedAt.Provider);
        var expiration = Task.Delay(TimeSpan.FromMilliseconds(InfiniteWaitMilliseconds), startedAt.Provider, cleanupDeadline.Token);
        ServerFailureObserver.Observe(() => KillIfRunning(process), failures);
        if (!joined.IsCompleted)
        { _ = await Task.WhenAny(joined, expiration).ConfigureAwait(false); }
        if (!joined.IsCompleted)
        {
            failures.Add(new TimeoutException(TimeoutFailure));
            return false;
        }
        return true;
    }

    private static TimeSpan RemainingPrimaryTime(TestElapsedClock startedAt, TimeSpan timeout)
    {
        var elapsed = startedAt.Elapsed;
        return elapsed >= timeout ? TimeSpan.Zero : timeout - elapsed;
    }

    private static TimeSpan RemainingCleanupTime(TestElapsedClock startedAt, TimeSpan timeout, TimeSpan settlement)
    {
        var elapsed = startedAt.Elapsed;
        if (elapsed <= timeout)
        { return settlement; }
        var elapsedAfterPrimaryDeadline = elapsed - timeout;
        return elapsedAfterPrimaryDeadline >= settlement ? TimeSpan.Zero : settlement - elapsedAfterPrimaryDeadline;
    }

    private static Result? MakeResult(Process process, CliBackupRestoreProcessOutput stdout,
        CliBackupRestoreProcessOutput stderr, Task? exit, Task? output, Task? error, Task joined)
    {
        if (!joined.IsCompletedSuccessfully || exit is null || output is null || error is null)
        { return null; }
        return new(process.ExitCode, stdout.Text, stderr.Text, exit.IsCompleted, output.IsCompleted,
            error.IsCompleted, Disposed: false);
    }

    private static void KillIfRunning(Process process)
    {
        try
        { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
        catch (InvalidOperationException) when (process.HasExited) { }
    }

    [DoesNotReturn]
    private static void FailStop()
    {
        try
        { Console.Error.WriteLine(FailStopMessage); }
        finally
        { Environment.Exit(FailStopExitCode); }
        throw new InvalidOperationException(FailStopMessage);
    }
}
