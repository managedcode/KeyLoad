using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensivePinnedImageProcess
{
    internal static async Task<TimeSeriesIntensivePinnedImageCommand> RunAsync(string executable,
        IReadOnlyList<string> arguments, int seconds, CancellationToken token,
        TimeSeriesIntensivePinnedImageEvidence? evidence = null, string? label = null)
    {
        using var process = new Process { StartInfo = StartInfo(executable, arguments), EnableRaisingEvents = true };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        using var output = new TimeSeriesIntensivePinnedImageOutput();
        using var error = new TimeSeriesIntensivePinnedImageOutput();
        var failures = new List<string>();
        var started = TimeProvider.System.GetUtcNow();
        if (!process.Start())
        {
            throw new InvalidOperationException(TimeSeriesIntensivePinnedImageProtocol.NativeFailure);
        }

        var stdout = output.ReadAsync(process.StandardOutput.BaseStream, deadline.Token);
        var stderr = error.ReadAsync(process.StandardError.BaseStream, deadline.Token);
        Exception? primary = null;
        TimeSeriesIntensivePinnedImageCommand? command = null;
        try
        {
            await AwaitAsync(process.WaitForExitAsync(deadline.Token), stdout, stderr);
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            command = await CompleteAsync(process, deadline, stdout, stderr, failures, primary,
                new(executable, arguments, seconds, null, started, started, [], [], []), output, error, evidence, label);
        }

        return command;
    }

    private static ProcessStartInfo StartInfo(string executable, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static async Task AwaitAsync(Task exit, Task output, Task error)
    {
        var pending = new List<Task> { exit, output, error };
        while (pending.Count != 0)
        {
            var finished = await Task.WhenAny(pending);
            await finished;
            pending.Remove(finished);
        }
    }

    private static async Task<Exception?> FinishAsync(Process process, CancellationTokenSource deadline,
        Task output, Task error, List<string> failures, Exception? primary)
    {
        try
        {
            await TimeSeriesIntensivePinnedImageReaper.ReapAsync(process);
        }
        catch (Exception cleanup) when (primary is not null || TimeSeriesIntensivePinnedImageFailure.IsNative(cleanup))
        {
            failures.Add(cleanup.GetType().FullName ?? nameof(Exception));
            primary ??= cleanup;
        }

        try
        {
            await deadline.CancelAsync();
        }
        catch (Exception cancellation) when (primary is not null || TimeSeriesIntensivePinnedImageFailure.IsNative(cancellation))
        {
            failures.Add(cancellation.GetType().FullName ?? nameof(Exception));
            primary ??= cancellation;
        }

        var outputFailure = await TimeSeriesIntensivePinnedImageOutput.ObserveAsync(output, failures, primary);
        var errorFailure = await TimeSeriesIntensivePinnedImageOutput.ObserveAsync(error, failures, primary);
        primary ??= outputFailure ?? errorFailure;
        return primary;
    }

    private static async Task<TimeSeriesIntensivePinnedImageCommand> CompleteAsync(Process process,
        CancellationTokenSource deadline, Task stdout, Task stderr, List<string> failures, Exception? primary,
        TimeSeriesIntensivePinnedImageCommand initial, TimeSeriesIntensivePinnedImageOutput output,
        TimeSeriesIntensivePinnedImageOutput error, TimeSeriesIntensivePinnedImageEvidence? evidence, string? label)
    {
        var failure = await FinishAsync(process, deadline, stdout, stderr, failures, primary);
        var command = initial with
        {
            ExitCode = process.HasExited ? process.ExitCode : null,
            CompletedAt = TimeProvider.System.GetUtcNow(),
            Output = output.Bytes,
            Error = error.Bytes,
            Failures = failures
        };
        failure = await RetainAsync(evidence, label, command, failure);
        if (primary is null && failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return command;
    }

    private static async Task<Exception?> RetainAsync(TimeSeriesIntensivePinnedImageEvidence? evidence,
        string? label, TimeSeriesIntensivePinnedImageCommand command, Exception? primary)
    {
        if (evidence is null || label is null)
        {
            return primary;
        }

        try
        {
            await evidence.RetainCommandAsync(label, command);
        }
        catch (Exception persistence) when (primary is not null || TimeSeriesIntensivePinnedImageFailure.IsNative(persistence))
        {
            primary ??= persistence;
        }

        return primary;
    }
}
