using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3FixtureArguments
{
    private const string ArgumentPrefix = "--";
    private const string Separator = "=";

    internal static string[] Create(NativeCoverageRf3FixtureContext context, string storageRoot,
        string startupPollMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(startupPollMilliseconds);
        var coverageRoot = Path.Combine(context.FixtureRoot, NativeCoverageRf3FixtureProtocol.CoverageDirectoryName);
        Directory.CreateDirectory(coverageRoot);
        var fields = new (string Name, string Value)[]
        {
            (NativeCoverageRf3FixtureProtocol.DataRootArgumentName, storageRoot),
            (NativeCoverageRf3FixtureProtocol.ModeSetting, string.Empty),
            (NativeCoverageRf3FixtureProtocol.SourceManifestSetting, string.Empty),
            (NativeCoverageRf3FixtureProtocol.ImageReferenceSetting, context.ImageReference),
            (NativeCoverageRf3FixtureProtocol.RunIdSetting, context.RunId),
            (NativeCoverageRf3FixtureProtocol.CoverageImageIdSetting, context.ImageId),
            (NativeCoverageRf3FixtureProtocol.ContextHashSetting, context.ContextManifestSha256),
            (NativeCoverageRf3FixtureProtocol.OutputRootSetting, coverageRoot),
            (NativeCoverageRf3FixtureProtocol.SourceReceiptHashSetting, context.SourceManifestSha256),
            (NativeCoverageRf3FixtureProtocol.ServerDllHashSetting, context.Server.DllSha256),
            (NativeCoverageRf3FixtureProtocol.ServerPdbHashSetting, context.Server.PdbSha256),
            (NativeCoverageRf3FixtureProtocol.ServerMvidSetting, context.Server.Mvid),
            (NativeCoverageRf3FixtureProtocol.ToolVersionSetting, context.Collector.Version),
            (NativeCoverageRf3FixtureProtocol.ToolClosureSetting, context.Collector.ClosureDigest),
            (NativeCoverageRf3FixtureProtocol.SettingsHashSetting, context.Collector.SettingsSha256),
            (NativeCoverageRf3FixtureProtocol.ShutdownSecondsSetting, Seconds(context.Bounds.ShutdownTimeout)),
            (NativeCoverageRf3FixtureProtocol.SettlementSecondsSetting, Seconds(context.Bounds.SettlementTimeout)),
            (NativeCoverageRf3FixtureProtocol.MaximumReportBytesSetting,
                context.Bounds.MaximumReportBytes.ToString(CultureInfo.InvariantCulture)),
            (NativeCoverageRf3FixtureProtocol.StartupPollSetting, startupPollMilliseconds)
        };
        return [.. fields.Select(field => ArgumentPrefix + field.Name + Separator + field.Value),
            ClusterFixtureProtocol.EphemeralArgument,
            ClusterFixtureProtocol.SnapshotThresholdArgument];
    }

    private static string Seconds(string value)
    {
        var duration = TimeSpan.ParseExact(value, "c", CultureInfo.InvariantCulture);
        if (duration.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        return checked((long)duration.TotalSeconds).ToString(CultureInfo.InvariantCulture);
    }
}
