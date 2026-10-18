using System.Globalization;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageClusterRunPolicyReader
{
    internal static NativeCoverageClusterExecutionBounds ReadBounds(JsonElement run)
    {
        var coverage = run.GetProperty(NativeCoverageClusterRunProtocol.ExecutionPolicyProperty)
            .GetProperty(NativeCoverageClusterRunProtocol.CoverageProperty);
        return new(coverage.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumDescriptorBytesProperty).GetInt32(),
            coverage.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumFilesProperty).GetInt32(),
            coverage.GetProperty(NativeCoverageClusterFixtureProtocol.ReadBufferBytesProperty).GetInt32(),
            coverage.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumTotalBytesProperty).GetInt64(),
            coverage.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumFileBytesProperty).GetInt64(),
            coverage.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumPathCharactersProperty).GetInt32(),
            coverage.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumManifestBytesProperty).GetInt64(),
            coverage.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumReportBytesProperty).GetInt64(),
            RequiredString(coverage, NativeCoverageClusterFixtureProtocol.ShutdownTimeoutProperty),
            RequiredString(coverage, NativeCoverageClusterFixtureProtocol.SettlementTimeoutProperty),
            RequiredString(coverage, NativeCoverageClusterFixtureProtocol.ContainerStopTimeoutProperty),
            RequiredString(coverage, NativeCoverageClusterFixtureProtocol.ApplicationCleanupTimeoutProperty));
    }

    internal static void Validate(JsonElement policy)
    {
        if (!NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(policy,
                NativeCoverageClusterRunProtocol.CoverageProperty, NativeCoverageClusterRunProtocol.TestsProperty))
        {
            throw Invalid();
        }
        var coverage = policy.GetProperty(NativeCoverageClusterRunProtocol.CoverageProperty);
        if (!HasCoverageProperties(coverage) || !ReadCoverageOptions(coverage).IsValid())
        {
            throw Invalid();
        }

        var tests = policy.GetProperty(NativeCoverageClusterRunProtocol.TestsProperty);
        if (!HasTestProperties(tests) || !ReadTestOptions(tests).IsValid())
        {
            throw Invalid();
        }
    }

    private static bool HasCoverageProperties(JsonElement coverage)
        => NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(coverage,
            NativeCoverageClusterFixtureProtocol.MaximumDescriptorBytesProperty,
            NativeCoverageClusterFixtureProtocol.MaximumFilesProperty,
            NativeCoverageClusterFixtureProtocol.ReadBufferBytesProperty,
            NativeCoverageClusterFixtureProtocol.MaximumTotalBytesProperty,
            NativeCoverageClusterFixtureProtocol.MaximumFileBytesProperty,
            NativeCoverageClusterFixtureProtocol.MaximumPathCharactersProperty,
            NativeCoverageClusterFixtureProtocol.MaximumManifestBytesProperty,
            NativeCoverageClusterFixtureProtocol.MaximumReportBytesProperty,
            NativeCoverageClusterFixtureProtocol.ShutdownTimeoutProperty,
            NativeCoverageClusterFixtureProtocol.SettlementTimeoutProperty,
            NativeCoverageClusterFixtureProtocol.ContainerStopTimeoutProperty,
            NativeCoverageClusterFixtureProtocol.ApplicationCleanupTimeoutProperty);

    private static NativeCoverageExecutionOptions ReadCoverageOptions(JsonElement value) => new()
    {
        MaximumDescriptorBytes = value.GetProperty(
            NativeCoverageClusterFixtureProtocol.MaximumDescriptorBytesProperty).GetInt32(),
        MaximumFiles = value.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumFilesProperty).GetInt32(),
        ReadBufferBytes = value.GetProperty(NativeCoverageClusterFixtureProtocol.ReadBufferBytesProperty).GetInt32(),
        MaximumTotalBytes = value.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumTotalBytesProperty).GetInt64(),
        MaximumFileBytes = value.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumFileBytesProperty).GetInt32(),
        MaximumPathCharacters = value.GetProperty(
            NativeCoverageClusterFixtureProtocol.MaximumPathCharactersProperty).GetInt32(),
        MaximumManifestBytes = value.GetProperty(
            NativeCoverageClusterFixtureProtocol.MaximumManifestBytesProperty).GetInt32(),
        MaximumReportBytes = value.GetProperty(NativeCoverageClusterFixtureProtocol.MaximumReportBytesProperty).GetInt32(),
        ShutdownTimeout = Duration(value, NativeCoverageClusterFixtureProtocol.ShutdownTimeoutProperty),
        SettlementTimeout = Duration(value, NativeCoverageClusterFixtureProtocol.SettlementTimeoutProperty),
        ContainerStopTimeout = Duration(value, NativeCoverageClusterFixtureProtocol.ContainerStopTimeoutProperty),
        ApplicationCleanupTimeout = Duration(value,
            NativeCoverageClusterFixtureProtocol.ApplicationCleanupTimeoutProperty)
    };

    private static bool HasTestProperties(JsonElement tests)
        => NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(tests,
            NativeCoverageClusterRunProtocol.OrdinaryTimeoutProperty,
            NativeCoverageClusterRunProtocol.ClusterTimeoutProperty,
            NativeCoverageClusterRunProtocol.IntensiveTimeoutProperty,
            NativeCoverageClusterRunProtocol.NativeControlTimeoutProperty,
            NativeCoverageClusterRunProtocol.NativeScaledTimeoutProperty,
            NativeCoverageClusterRunProtocol.NativeVectorTimeoutProperty,
            NativeCoverageClusterFixtureProtocol.ApplicationCleanupTimeoutProperty,
            NativeCoverageClusterRunProtocol.ImageCleanupTimeoutProperty,
            NativeCoverageClusterRunProtocol.TerminationGraceProperty,
            NativeCoverageClusterRunProtocol.ProcessSettlementTimeoutProperty,
            NativeCoverageClusterRunProtocol.ProcessExitPollIntervalProperty,
            NativeCoverageClusterRunProtocol.CleanupOutputCharactersProperty,
            NativeCoverageClusterRunProtocol.MaximumFilterCharactersProperty,
            NativeCoverageClusterFixtureProtocol.MaximumPathCharactersProperty);

    private static TestExecutionOptions ReadTestOptions(JsonElement value) => new()
    {
        OrdinaryTimeout = Duration(value, NativeCoverageClusterRunProtocol.OrdinaryTimeoutProperty),
        ClusterTimeout = Duration(value, NativeCoverageClusterRunProtocol.ClusterTimeoutProperty),
        IntensiveTimeout = Duration(value, NativeCoverageClusterRunProtocol.IntensiveTimeoutProperty),
        NativeControlTimeout = Duration(value, NativeCoverageClusterRunProtocol.NativeControlTimeoutProperty),
        NativeScaledTimeout = Duration(value, NativeCoverageClusterRunProtocol.NativeScaledTimeoutProperty),
        NativeVectorTimeout = Duration(value, NativeCoverageClusterRunProtocol.NativeVectorTimeoutProperty),
        ApplicationCleanupTimeout = Duration(value,
            NativeCoverageClusterFixtureProtocol.ApplicationCleanupTimeoutProperty),
        ImageCleanupTimeout = Duration(value, NativeCoverageClusterRunProtocol.ImageCleanupTimeoutProperty),
        TerminationGrace = Duration(value, NativeCoverageClusterRunProtocol.TerminationGraceProperty),
        ProcessSettlementTimeout = Duration(value, NativeCoverageClusterRunProtocol.ProcessSettlementTimeoutProperty),
        ProcessExitPollInterval = Duration(value, NativeCoverageClusterRunProtocol.ProcessExitPollIntervalProperty),
        CleanupOutputCharacters = value.GetProperty(
            NativeCoverageClusterRunProtocol.CleanupOutputCharactersProperty).GetInt32(),
        MaximumFilterCharacters = value.GetProperty(
            NativeCoverageClusterRunProtocol.MaximumFilterCharactersProperty).GetInt32(),
        MaximumPathCharacters = value.GetProperty(
            NativeCoverageClusterFixtureProtocol.MaximumPathCharactersProperty).GetInt32()
    };

    private static TimeSpan Duration(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        if (value.ValueKind != JsonValueKind.String
            || !TimeSpan.TryParseExact(value.GetString(), NativeCoverageClusterRunProtocol.TimeSpanFormat,
                CultureInfo.InvariantCulture, out var duration))
        {
            throw Invalid();
        }
        return duration;
    }

    private static string RequiredString(JsonElement element, string property)
    {
        var value = element.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(value) ? throw Invalid() : value;
    }

    private static InvalidOperationException Invalid() =>
        new(NativeCoverageClusterFixtureProtocol.InvalidRun);
}
