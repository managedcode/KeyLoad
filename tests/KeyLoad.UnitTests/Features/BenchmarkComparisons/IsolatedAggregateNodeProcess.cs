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
    internal const string StartFailure = "The isolated aggregate Node child did not start.";
    internal const int TimeoutSeconds = 60;
    internal const int CleanupSeconds = 5;

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
        var startInfo = StartInfo(arguments);
        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(StartFailure);
            }
        }
        catch (Exception startFailure)
        {
            try
            {
                process.Dispose();
            }
            catch (Exception disposeFailure)
            {
                throw new AggregateException("Native Node start and process-owner cleanup failed.",
                    startFailure, disposeFailure);
            }
            throw;
        }

        return await IsolatedAggregateNodeLifetime.RunAsync(
            process, cancellationToken, TimeSpan.FromSeconds(TimeoutSeconds), TimeSpan.FromSeconds(CleanupSeconds));
    }

    internal static ProcessStartInfo StartInfo(IEnumerable<string> arguments)
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

}
