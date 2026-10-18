using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed record NativeCoverageClusterClusterImage(string Tag, string RunId, string ImageId,
    string ContextDigest, string SourceReceiptSha256, string OutputRoot, string ServerDllSha256,
    string ServerPdbSha256, string ServerMvid, string ToolVersion, string ToolClosureSha256,
    string SettingsSha256, string ShutdownSeconds, string SettlementSeconds, string MaximumReportBytes,
    string StartupPollMilliseconds, string StartupTimeoutMilliseconds)
{
    internal static NativeCoverageClusterClusterImage? Read(IOptions<NativeCoverageClusterImageOptions> selectionOptions,
        bool ephemeral, int? benchmarkNodeCount)
    {
        ArgumentNullException.ThrowIfNull(selectionOptions);
        var selection = selectionOptions.Value;
        if (!selection.IsValid())
        {
            throw Invalid();
        }
        if (!selection.HasSelection)
        {
            return null;
        }
        if (!selection.IsNestedSelection || !ephemeral || benchmarkNodeCount is not null)
        {
            throw Invalid();
        }
        return new(selection.ExpectedImageTag, selection.RunId!, selection.ImageId!,
            selection.ContextManifestSha256!, selection.SourceReceiptSha256!, selection.GetFullOutputRoot(),
            selection.ServerDllSha256!, selection.ServerPdbSha256!, selection.ServerMvid!, selection.ToolVersion!,
            selection.ToolClosureSha256!, selection.SettingsSha256!, selection.ShutdownSeconds!,
            selection.SettlementSeconds!, selection.MaximumReportBytes!, selection.StartupPollMilliseconds!,
            selection.GetStartupTimeoutMilliseconds());
    }

    internal string PrepareOutputDirectory(string name)
    {
        var directory = Path.Combine(OutputRoot, name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal void Apply(IResourceBuilder<ContainerResource> resource, string name)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        resource.WithEnvironment(NativeCoverageClusterProtocol.CoverageNodeEnvironment, name)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageSessionEnvironment, RunId)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageContextEnvironment, ContextDigest)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageImageIdEnvironment, ImageId)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageSourceEnvironment, SourceReceiptSha256)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageDllHashEnvironment, ServerDllSha256)
            .WithEnvironment(NativeCoverageClusterProtocol.CoveragePdbHashEnvironment, ServerPdbSha256)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageMvidEnvironment, ServerMvid)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageToolVersionEnvironment, ToolVersion)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageToolClosureEnvironment, ToolClosureSha256)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageSettingsHashEnvironment, SettingsSha256)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageSettingsPathEnvironment,
                NativeCoverageClusterProtocol.CoverageSettingsPath)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageOutputEnvironment,
                NativeCoverageClusterProtocol.CoverageOutputDirectory)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageShutdownSecondsEnvironment, ShutdownSeconds)
            .WithEnvironment(NativeCoverageClusterProtocol.SettlementEnvironment, SettlementSeconds)
            .WithEnvironment(NativeCoverageClusterProtocol.CoverageMaxReportEnvironment, MaximumReportBytes)
            .WithEnvironment(NativeCoverageClusterProtocol.StartupPollEnvironment, StartupPollMilliseconds)
            .WithEnvironment(NativeCoverageClusterProtocol.StartupTimeoutEnvironment, StartupTimeoutMilliseconds);
    }

    private static InvalidOperationException Invalid()
        => new(NativeCoverageClusterProtocol.InvalidSelection);
}
