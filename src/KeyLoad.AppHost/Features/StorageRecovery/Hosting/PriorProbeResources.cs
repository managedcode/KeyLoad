namespace KeyLoad.AppHost.Features.StorageRecovery;

/// <summary>Owns preparation of exact stopped-format test executables before recovery admission.</summary>
internal static class PriorProbeResources
{
    internal const string DirectoryEnvironment = "KeyLoadTests__PriorProbesDirectory";
    internal const string Native5Resource = "prepare-native5-probe";
    internal const string Native6Resource = "prepare-native6-probe";
    internal const string RecoverySuite = "recovery";
    private const string Shell = "bash";
    private const string Script = "scripts/Features/StorageRecovery/build-prior-probe.sh";
    private const string ArtifactDirectory = "prior-probes";
    private const string Native5Directory = "native5-probe";
    private const string Native6Directory = "native6-probe";
    private const string Epoch5Argument = "--epoch=5";
    private const string Epoch6Argument = "--epoch=6";
    private const string DestinationArgument = "--destination=";
    private const string DirectoryIdFormat = "N";

    internal static string CreateDirectoryPath(string resultsDirectory)
        => Path.Combine(resultsDirectory, ArtifactDirectory, Guid.NewGuid().ToString(DirectoryIdFormat));

    internal static IResourceBuilder<ExecutableResource>[] Add(
        IDistributedApplicationBuilder builder, string root, string artifactDirectory)
        =>
        [
            builder.AddExecutable(Native5Resource, Shell, root,
                [Script, Epoch5Argument, DestinationArgument + Path.Combine(artifactDirectory, Native5Directory)]),
            builder.AddExecutable(Native6Resource, Shell, root,
                [Script, Epoch6Argument, DestinationArgument + Path.Combine(artifactDirectory, Native6Directory)])
        ];
}
