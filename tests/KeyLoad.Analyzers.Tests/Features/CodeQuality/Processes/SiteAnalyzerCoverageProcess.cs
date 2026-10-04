using System.Diagnostics;
using System.Text;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal static class SiteAnalyzerCoverageProcess
{
    private static readonly SemaphoreSlim ProcessSlots = new(
        SiteAnalyzerCoverageTokens.MaximumConcurrentPowerShellProcesses,
        SiteAnalyzerCoverageTokens.MaximumConcurrentPowerShellProcesses);

    internal static Task<(int ExitCode, string StandardOutput, string StandardError)> ReadPowerShellRuntimeAsync(string workingDirectory) =>
        RunAsync(
            [
                SiteAnalyzerCoverageTokens.NoLogo,
                SiteAnalyzerCoverageTokens.NoProfile,
                SiteAnalyzerCoverageTokens.NonInteractive,
                SiteAnalyzerCoverageTokens.CommandArgument,
                SiteAnalyzerCoverageTokens.PowerShellRuntimeCommand
            ],
            workingDirectory);

    internal static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        using var timeout = new CancellationTokenSource(SiteAnalyzerCoverageTokens.ProcessTimeoutMilliseconds);
        await ProcessSlots.WaitAsync(timeout.Token);
        try
        {
            return await RunProcessAsync(arguments, workingDirectory, environmentOverrides, timeout.Token);
        }
        finally
        {
            ProcessSlots.Release();
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentOverrides,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(SiteAnalyzerCoverageTokens.ProcessName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        { start.ArgumentList.Add(argument); }
        if (environmentOverrides is not null)
        {
            foreach (var (name, value) in environmentOverrides)
            {
                if (value is null)
                { start.Environment.Remove(name); }
                else
                { start.Environment[name] = value; }
            }
        }

        using var process = new Process { StartInfo = start };
        process.Start();
        var standardOutput = ReadBoundedAsync(process.StandardOutput, cancellationToken);
        var standardError = ReadBoundedAsync(process.StandardError, cancellationToken);
        var exit = process.WaitForExitAsync(cancellationToken);
        try
        {
            await WaitForProcessAndCaptureAsync(exit, standardOutput, standardError);
            return (process.ExitCode, await standardOutput, await standardError);
        }
        catch (Exception error) when (IsProcessCaptureFailure(error))
        {
            await StopProcessAsync(process);
            throw;
        }
        finally
        {
            await StopProcessAsync(process);
            await ObserveCaptureAsync(standardOutput);
            await ObserveCaptureAsync(standardError);
        }
    }

    private static async Task WaitForProcessAndCaptureAsync(
        Task exit,
        Task<string> standardOutput,
        Task<string> standardError)
    {
        while (!exit.IsCompleted)
        {
            var tasks = new List<Task> { exit };
            if (!standardOutput.IsCompleted)
            { tasks.Add(standardOutput); }
            if (!standardError.IsCompleted)
            { tasks.Add(standardError); }
            var completed = await Task.WhenAny(tasks);
            if (completed == standardOutput)
            { await standardOutput; }
            if (completed == standardError)
            { await standardError; }
        }

        await exit;
        await standardOutput;
        await standardError;
    }

    private static bool IsProcessCaptureFailure(Exception error) =>
        error is InvalidOperationException or IOException or OperationCanceledException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException or
            System.Security.SecurityException or System.Text.DecoderFallbackException or AggregateException;

    private static bool IsNonFatalCleanupFailure(Exception error) =>
        error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[SiteAnalyzerCoverageTokens.ProcessOutputBufferSize];
        while (true)
        {
            var count = await reader.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            { return output.ToString(); }
            if (output.Length + count > SiteAnalyzerCoverageTokens.MaximumProcessOutputCharacters)
            {
                throw new InvalidOperationException(SiteAnalyzerCoverageTokens.ProcessOutputLimitMessage);
            }

            output.Append(buffer, 0, count);
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var cleanupTimeout = new CancellationTokenSource(SiteAnalyzerCoverageTokens.CleanupTimeoutMilliseconds);
                await process.WaitForExitAsync(cleanupTimeout.Token);
            }
        }
        catch (Exception error) when (IsNonFatalCleanupFailure(error))
        {
            // Preserve the original process, timeout, or capture failure.
        }
    }

    private static async Task ObserveCaptureAsync(Task<string> capture)
    {
        try
        {
            using var timeout = new CancellationTokenSource(SiteAnalyzerCoverageTokens.CleanupTimeoutMilliseconds);
            await capture.WaitAsync(timeout.Token);
        }
        catch (Exception error) when (IsNonFatalCleanupFailure(error))
        {
            // Capture completion is best-effort after the child has been stopped.
        }
    }
}
