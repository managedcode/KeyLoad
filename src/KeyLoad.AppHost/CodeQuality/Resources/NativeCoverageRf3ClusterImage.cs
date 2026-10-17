using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed record NativeCoverageRf3ClusterImage(string Tag, string RunId, string ImageId,
    string ContextDigest, string SourceReceiptSha256, string OutputRoot, string ServerDllSha256,
    string ServerPdbSha256, string ServerMvid, string ToolVersion, string ToolClosureSha256,
    string SettingsSha256, string ShutdownSeconds, string SettlementSeconds, string MaximumReportBytes,
    string StartupPollMilliseconds, string StartupTimeoutMilliseconds)
{
    internal static NativeCoverageRf3ClusterImage? Read(IOptions<NativeCoverageRf3ImageOptions> selectionOptions,
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
        resource.WithEnvironment(NativeCoverageRf3Protocol.CoverageNodeEnvironment, name)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageSessionEnvironment, RunId)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageContextEnvironment, ContextDigest)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageImageIdEnvironment, ImageId)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageSourceEnvironment, SourceReceiptSha256)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageDllHashEnvironment, ServerDllSha256)
            .WithEnvironment(NativeCoverageRf3Protocol.CoveragePdbHashEnvironment, ServerPdbSha256)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageMvidEnvironment, ServerMvid)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageToolVersionEnvironment, ToolVersion)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageToolClosureEnvironment, ToolClosureSha256)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageSettingsHashEnvironment, SettingsSha256)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageSettingsPathEnvironment,
                NativeCoverageRf3Protocol.CoverageSettingsPath)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageOutputEnvironment,
                NativeCoverageRf3Protocol.CoverageOutputDirectory)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageShutdownSecondsEnvironment, ShutdownSeconds)
            .WithEnvironment(NativeCoverageRf3Protocol.SettlementEnvironment, SettlementSeconds)
            .WithEnvironment(NativeCoverageRf3Protocol.CoverageMaxReportEnvironment, MaximumReportBytes)
            .WithEnvironment(NativeCoverageRf3Protocol.StartupPollEnvironment, StartupPollMilliseconds)
            .WithEnvironment(NativeCoverageRf3Protocol.StartupTimeoutEnvironment, StartupTimeoutMilliseconds);
    }

    private static InvalidOperationException Invalid()
        => new(NativeCoverageRf3Protocol.InvalidSelection);
}
