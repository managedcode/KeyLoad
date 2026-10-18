using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageClusterFixtureArguments
{
    private const string ArgumentPrefix = "--";
    private const string Separator = "=";

    internal static string[] Create(NativeCoverageClusterFixtureContext context, string storageRoot,
        string startupPollMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(startupPollMilliseconds);
        var coverageRoot = Path.Combine(context.FixtureRoot, NativeCoverageClusterFixtureProtocol.CoverageDirectoryName);
        Directory.CreateDirectory(coverageRoot);
        var fields = new (string Name, string Value)[]
        {
            (NativeCoverageClusterFixtureProtocol.DataRootArgumentName, storageRoot),
            (NativeCoverageClusterFixtureProtocol.ModeSetting, string.Empty),
            (NativeCoverageClusterFixtureProtocol.SourceManifestSetting, string.Empty),
            (NativeCoverageClusterFixtureProtocol.ImageReferenceSetting, context.ImageReference),
            (NativeCoverageClusterFixtureProtocol.RunIdSetting, context.RunId),
            (NativeCoverageClusterFixtureProtocol.CoverageImageIdSetting, context.ImageId),
            (NativeCoverageClusterFixtureProtocol.ContextHashSetting, context.ContextManifestSha256),
            (NativeCoverageClusterFixtureProtocol.OutputRootSetting, coverageRoot),
            (NativeCoverageClusterFixtureProtocol.SourceReceiptHashSetting, context.SourceManifestSha256),
            (NativeCoverageClusterFixtureProtocol.ServerDllHashSetting, context.Server.DllSha256),
            (NativeCoverageClusterFixtureProtocol.ServerPdbHashSetting, context.Server.PdbSha256),
            (NativeCoverageClusterFixtureProtocol.ServerMvidSetting, context.Server.Mvid),
            (NativeCoverageClusterFixtureProtocol.ToolVersionSetting, context.Collector.Version),
            (NativeCoverageClusterFixtureProtocol.ToolClosureSetting, context.Collector.ClosureDigest),
            (NativeCoverageClusterFixtureProtocol.SettingsHashSetting, context.Collector.SettingsSha256),
            (NativeCoverageClusterFixtureProtocol.ShutdownSecondsSetting, Seconds(context.Bounds.ShutdownTimeout)),
            (NativeCoverageClusterFixtureProtocol.SettlementSecondsSetting, Seconds(context.Bounds.SettlementTimeout)),
            (NativeCoverageClusterFixtureProtocol.MaximumReportBytesSetting,
                context.Bounds.MaximumReportBytes.ToString(CultureInfo.InvariantCulture)),
            (NativeCoverageClusterFixtureProtocol.StartupPollSetting, startupPollMilliseconds)
        };
        return [.. fields.Select(field => ArgumentPrefix + field.Name + Separator + field.Value),
            ClusterFixtureProtocol.EphemeralArgument,
            ClusterFixtureProtocol.SiloCountArgument, ClusterFixtureProtocol.ReplicaCountArgument];
    }

    private static string Seconds(string value)
    {
        var duration = TimeSpan.ParseExact(value, "c", CultureInfo.InvariantCulture);
        if (duration.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        return checked((long)duration.TotalSeconds).ToString(CultureInfo.InvariantCulture);
    }
}
