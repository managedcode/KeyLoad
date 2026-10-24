using System.Diagnostics;
using System.Reflection;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>Constructs the real host launch without inherited benchmark secrets.</summary>
internal static class ComparisonHostLaunchSettings
{
    private const string MissingBuildConfiguration = "The execution test assembly build configuration is absent.";
    private const string TreenodeFilterArgument = "--treenode-filter";
    private const string WorkloadFilter = "/*/*/BenchmarkSessionWorkloadTests/*";
    private const string OutputArgument = "--output";
    private const string DetailedOutput = "Detailed";
    private const string ListTestsArgument = "--list-tests";
    private const char AssignmentSeparator = '=';
    private const string ArgumentPrefix = "--";
    private const int ArgumentPrefixLength = 2;
    private const string InvalidConfigurationAssignment = "The execution workload test requires configuration assignments.";
    private const string ConfigurationSeparator = ":";
    private const string EnvironmentSeparator = "__";

    private const string DotnetCommand = "dotnet";
    private const string SolutionName = "KeyLoad.slnx";
    private const string BenchmarksDirectory = "benchmarks";
    private const string HostDirectory = "KeyLoad.ComparisonHost";
    private const string HostProjectName = "KeyLoad.ComparisonHost.csproj";
    private const string HostAssemblyName = "KeyLoad.ComparisonHost.dll";
    private const string BuildOutputDirectory = "bin";
    private const string TargetFramework = "net10.0";
    private const string BenchmarkEnvironmentPrefix = "Benchmarks__";
    private const string ConnectionEnvironmentPrefix = "ConnectionStrings__";
    private const string BenchmarkColonEnvironmentPrefix = "Benchmarks:";
    private const string ConnectionColonEnvironmentPrefix = "ConnectionStrings:";
    private const string MissingRepositoryMessage = "The KeyLoad repository root is unavailable.";
    private const string MissingHostMessage = "The execution comparison host is not built.";
    private const string MaximumParallelTestsArgument = "--maximum-parallel-tests";
    private const string ExclusiveMeasurementParallelism = "1";

    internal static ProcessStartInfo Create(string[] arguments, IReadOnlyDictionary<string, string>? environment)
    {
        var root = RepositoryRoot();
        var configuration = typeof(ComparisonHostLaunchSettings).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
        if (string.IsNullOrWhiteSpace(configuration))
        {
            throw new InvalidOperationException(MissingBuildConfiguration);
        }
        var assembly = Path.Combine(root, BenchmarksDirectory, HostDirectory, BuildOutputDirectory,
            configuration, TargetFramework, HostAssemblyName);
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException(MissingHostMessage, assembly);
        }

        var startInfo = new ProcessStartInfo(DotnetCommand)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        startInfo.ArgumentList.Add(assembly);
        startInfo.ArgumentList.Add(TreenodeFilterArgument);
        startInfo.ArgumentList.Add(WorkloadFilter);
        startInfo.ArgumentList.Add(OutputArgument);
        startInfo.ArgumentList.Add(DetailedOutput);
        startInfo.ArgumentList.Add(MaximumParallelTestsArgument);
        startInfo.ArgumentList.Add(ExclusiveMeasurementParallelism);
        foreach (var key in startInfo.Environment.Keys.Where(IsBenchmarkSetting).ToArray())
        {
            startInfo.Environment.Remove(key);
        }
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }
        foreach (var argument in arguments)
        {
            if (argument == ListTestsArgument)
            {
                startInfo.ArgumentList.Add(argument);
                continue;
            }
            var separator = argument.IndexOf(AssignmentSeparator, StringComparison.Ordinal);
            if (!argument.StartsWith(ArgumentPrefix, StringComparison.Ordinal) || separator <= ArgumentPrefixLength)
            {
                throw new InvalidOperationException(InvalidConfigurationAssignment);
            }
            startInfo.Environment[argument[ArgumentPrefixLength..separator].Replace(ConfigurationSeparator, EnvironmentSeparator, StringComparison.Ordinal)] = argument[(separator + 1)..];
        }
        return startInfo;
    }

    private static bool IsBenchmarkSetting(string key)
        => key.StartsWith(BenchmarkEnvironmentPrefix, StringComparison.OrdinalIgnoreCase)
           || key.StartsWith(ConnectionEnvironmentPrefix, StringComparison.OrdinalIgnoreCase)
           || key.StartsWith(BenchmarkColonEnvironmentPrefix, StringComparison.OrdinalIgnoreCase)
           || key.StartsWith(ConnectionColonEnvironmentPrefix, StringComparison.OrdinalIgnoreCase);

    private static string RepositoryRoot()
    {
        foreach (var path in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, SolutionName))
                    && File.Exists(Path.Combine(directory.FullName, BenchmarksDirectory, HostDirectory, HostProjectName)))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException(MissingRepositoryMessage);
    }
}
