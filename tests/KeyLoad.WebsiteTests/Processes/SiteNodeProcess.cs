using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Runs one repository Node script to completion; caller cancellation stops and joins the whole process tree.</summary>
internal static class SiteNodeProcess
{
    internal static async Task<SiteNodeResult> RunAsync(string script, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(SiteTokens.Node)
        {
            WorkingDirectory = SiteResultFiles.Repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(SiteResultFiles.Repository, script));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The Node process did not start.");
        // Both pipes drain until the process exits, so neither can block the child.
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(process);
            await Task.WhenAll(output, error);
            throw;
        }

        return new(process.ExitCode, await output, await error);
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // The child exited between cancellation and termination.
        }
        catch (Win32Exception) when (process.HasExited)
        {
            // The child exited while its tree was being terminated.
        }

        await process.WaitForExitAsync(CancellationToken.None);
    }
}
