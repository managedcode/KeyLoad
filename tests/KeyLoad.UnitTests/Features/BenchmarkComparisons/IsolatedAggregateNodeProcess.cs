using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeProcess
{
    private const string Node = "node";
    private const string PathVariable = "PATH";
    private const string SolutionFile = "KeyLoad.slnx";
    private const string ScriptsDirectory = "scripts";
    private const string FeaturesDirectory = "Features";
    private const string SliceDirectory = "BenchmarkComparisons";
    private const string StartFailure = "The isolated aggregate Node child did not start.";
    private const int TimeoutSeconds = 60;
    private const int CleanupSeconds = 5;

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(StartFailure);
    }

    internal static string Module(string name)
        => Path.Combine(RepositoryRoot(), ScriptsDirectory, FeaturesDirectory, SliceDirectory, name);

    internal static async Task<IsolatedAggregateNodeResult> RunAsync(IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo(arguments) };
        if (!process.Start())
        {
            throw new InvalidOperationException(StartFailure);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        var output = IsolatedAggregateNodeOutput.ReadAsync(process.StandardOutput, deadline.Token);
        var error = IsolatedAggregateNodeOutput.ReadAsync(process.StandardError, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return new(process.ExitCode, await output, await error);
        }
        finally
        {
            try
            {
                await ReapAsync(process);
            }
            finally
            {
                await deadline.CancelAsync();
                await IsolatedAggregateNodeOutput.ObserveAsync(output);
                await IsolatedAggregateNodeOutput.ObserveAsync(error);
            }
        }
    }

    private static ProcessStartInfo StartInfo(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Node)
        {
            WorkingDirectory = RepositoryRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var path = Environment.GetEnvironmentVariable(PathVariable);
        start.Environment.Clear();
        if (!string.IsNullOrEmpty(path))
        {
            start.Environment[PathVariable] = path;
        }

        return start;
    }

    private static async Task ReapAsync(Process process)
    {
        TryKill(process);

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds));
        try
        {
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(StartFailure);
        }
    }

    private static void TryKill(Process process)
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
            // The owned child exited between the observation and kill.
        }
        catch (Win32Exception) when (process.HasExited)
        {
            // The operating system has already reaped the owned child.
        }
    }
}
