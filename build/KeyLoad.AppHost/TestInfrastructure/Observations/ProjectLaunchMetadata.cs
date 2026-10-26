using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.TestInfrastructure;

/// <summary>Reads the same execution SDK run properties used by Aspire's project launch.</summary>
internal static class ProjectLaunchMetadata
{
    internal const string ConfigurationEnvironmentVariable = "KEYLOAD_TEST_CONFIGURATION";
    private const string MissingConfiguration = "The AppHost build configuration is absent.";
    private const string MissingMetadata = "The execution SDK project launch metadata is absent.";
    private const string InvalidAssemblyPath = "The execution SDK managed entry assembly is not absolute.";
    private const string InvalidExecutablePath = "The execution SDK project executable is not absolute.";
    private const string MsBuildCommand = "msbuild";
    private const string NoLogoArgument = "-nologo";
    private const string RunMetadataTarget = "-target:ComputeRunArguments";
    private const string RunMetadataProperties = "-getProperty:TargetPath,RunCommand";
    private const string ConfigurationArgumentPrefix = "-property:Configuration=";
    private const string DotnetCommand = "dotnet";

    internal static string Configuration { get; } = typeof(ProjectLaunchMetadata).Assembly
        .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
        ?? throw new InvalidDataException(MissingConfiguration);

    internal static async Task<(string Assembly, string Executable)> ReadAsync(string project, string directory,
        string dotnet, IOptions<TestExecutionOptions> options, CancellationToken token)
    {
        var start = new ProcessStartInfo(dotnet) { WorkingDirectory = directory };
        foreach (var argument in new[] { MsBuildCommand, project, NoLogoArgument, RunMetadataTarget,
            RunMetadataProperties, ConfigurationArgumentPrefix + Configuration })
        {
            start.ArgumentList.Add(argument);
        }
        var output = await ProjectProcessObservationCommand.ReadAsync(start, options, options.Value.OrdinaryTimeout, token)
            .ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<Evaluation>(output)
            ?? throw new InvalidDataException(MissingMetadata);
        if (!Path.IsPathFullyQualified(result.Properties.TargetPath))
        {
            throw new InvalidDataException(InvalidAssemblyPath);
        }
        var command = result.Properties.RunCommand == DotnetCommand ? dotnet : result.Properties.RunCommand;
        if (!Path.IsPathFullyQualified(command))
        {
            throw new InvalidDataException(InvalidExecutablePath);
        }
        return (result.Properties.TargetPath, command);
    }

    private sealed record Evaluation(Properties Properties);
    private sealed record Properties(string TargetPath, string RunCommand);
}
