using System.Globalization;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3RunPolicyReader
{
    internal static NativeCoverageRf3ExecutionBounds ReadBounds(JsonElement run)
    {
        var coverage = run.GetProperty(NativeCoverageRf3RunProtocol.ExecutionPolicyProperty)
            .GetProperty(NativeCoverageRf3RunProtocol.CoverageProperty);
        return new(coverage.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumDescriptorBytesProperty).GetInt32(),
            coverage.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumFilesProperty).GetInt32(),
            coverage.GetProperty(NativeCoverageRf3FixtureProtocol.ReadBufferBytesProperty).GetInt32(),
            coverage.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumTotalBytesProperty).GetInt64(),
            coverage.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumFileBytesProperty).GetInt64(),
            coverage.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumPathCharactersProperty).GetInt32(),
            coverage.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumManifestBytesProperty).GetInt64(),
            coverage.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumReportBytesProperty).GetInt64(),
            RequiredString(coverage, NativeCoverageRf3FixtureProtocol.ShutdownTimeoutProperty),
            RequiredString(coverage, NativeCoverageRf3FixtureProtocol.SettlementTimeoutProperty),
            RequiredString(coverage, NativeCoverageRf3FixtureProtocol.ContainerStopTimeoutProperty),
            RequiredString(coverage, NativeCoverageRf3FixtureProtocol.ApplicationCleanupTimeoutProperty));
    }

    internal static void Validate(JsonElement policy)
    {
        if (!NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(policy,
                NativeCoverageRf3RunProtocol.CoverageProperty, NativeCoverageRf3RunProtocol.TestsProperty))
        {
            throw Invalid();
        }
        var coverage = policy.GetProperty(NativeCoverageRf3RunProtocol.CoverageProperty);
        if (!HasCoverageProperties(coverage) || !ReadCoverageOptions(coverage).IsValid())
        {
            throw Invalid();
        }

        var tests = policy.GetProperty(NativeCoverageRf3RunProtocol.TestsProperty);
        if (!HasTestProperties(tests) || !ReadTestOptions(tests).IsValid())
        {
            throw Invalid();
        }
    }

    private static bool HasCoverageProperties(JsonElement coverage)
        => NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(coverage,
            NativeCoverageRf3FixtureProtocol.MaximumDescriptorBytesProperty,
            NativeCoverageRf3FixtureProtocol.MaximumFilesProperty,
            NativeCoverageRf3FixtureProtocol.ReadBufferBytesProperty,
            NativeCoverageRf3FixtureProtocol.MaximumTotalBytesProperty,
            NativeCoverageRf3FixtureProtocol.MaximumFileBytesProperty,
            NativeCoverageRf3FixtureProtocol.MaximumPathCharactersProperty,
            NativeCoverageRf3FixtureProtocol.MaximumManifestBytesProperty,
            NativeCoverageRf3FixtureProtocol.MaximumReportBytesProperty,
            NativeCoverageRf3FixtureProtocol.ShutdownTimeoutProperty,
            NativeCoverageRf3FixtureProtocol.SettlementTimeoutProperty,
            NativeCoverageRf3FixtureProtocol.ContainerStopTimeoutProperty,
            NativeCoverageRf3FixtureProtocol.ApplicationCleanupTimeoutProperty);

    private static NativeCoverageExecutionOptions ReadCoverageOptions(JsonElement value) => new()
    {
        MaximumDescriptorBytes = value.GetProperty(
            NativeCoverageRf3FixtureProtocol.MaximumDescriptorBytesProperty).GetInt32(),
        MaximumFiles = value.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumFilesProperty).GetInt32(),
        ReadBufferBytes = value.GetProperty(NativeCoverageRf3FixtureProtocol.ReadBufferBytesProperty).GetInt32(),
        MaximumTotalBytes = value.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumTotalBytesProperty).GetInt64(),
        MaximumFileBytes = value.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumFileBytesProperty).GetInt32(),
        MaximumPathCharacters = value.GetProperty(
            NativeCoverageRf3FixtureProtocol.MaximumPathCharactersProperty).GetInt32(),
        MaximumManifestBytes = value.GetProperty(
            NativeCoverageRf3FixtureProtocol.MaximumManifestBytesProperty).GetInt32(),
        MaximumReportBytes = value.GetProperty(NativeCoverageRf3FixtureProtocol.MaximumReportBytesProperty).GetInt32(),
        ShutdownTimeout = Duration(value, NativeCoverageRf3FixtureProtocol.ShutdownTimeoutProperty),
        SettlementTimeout = Duration(value, NativeCoverageRf3FixtureProtocol.SettlementTimeoutProperty),
        ContainerStopTimeout = Duration(value, NativeCoverageRf3FixtureProtocol.ContainerStopTimeoutProperty),
        ApplicationCleanupTimeout = Duration(value,
            NativeCoverageRf3FixtureProtocol.ApplicationCleanupTimeoutProperty)
    };

    private static bool HasTestProperties(JsonElement tests)
        => NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(tests,
            NativeCoverageRf3RunProtocol.OrdinaryTimeoutProperty,
            NativeCoverageRf3RunProtocol.ClusterTimeoutProperty,
            NativeCoverageRf3RunProtocol.IntensiveTimeoutProperty,
            NativeCoverageRf3RunProtocol.NativeControlTimeoutProperty,
            NativeCoverageRf3RunProtocol.NativeScaledTimeoutProperty,
            NativeCoverageRf3RunProtocol.NativeVectorTimeoutProperty,
            NativeCoverageRf3FixtureProtocol.ApplicationCleanupTimeoutProperty,
            NativeCoverageRf3RunProtocol.ImageCleanupTimeoutProperty,
            NativeCoverageRf3RunProtocol.TerminationGraceProperty,
            NativeCoverageRf3RunProtocol.ProcessSettlementTimeoutProperty,
            NativeCoverageRf3RunProtocol.ProcessExitPollIntervalProperty,
            NativeCoverageRf3RunProtocol.CleanupOutputCharactersProperty,
            NativeCoverageRf3RunProtocol.MaximumFilterCharactersProperty,
            NativeCoverageRf3FixtureProtocol.MaximumPathCharactersProperty);

    private static TestExecutionOptions ReadTestOptions(JsonElement value) => new()
    {
        OrdinaryTimeout = Duration(value, NativeCoverageRf3RunProtocol.OrdinaryTimeoutProperty),
        ClusterTimeout = Duration(value, NativeCoverageRf3RunProtocol.ClusterTimeoutProperty),
        IntensiveTimeout = Duration(value, NativeCoverageRf3RunProtocol.IntensiveTimeoutProperty),
        NativeControlTimeout = Duration(value, NativeCoverageRf3RunProtocol.NativeControlTimeoutProperty),
        NativeScaledTimeout = Duration(value, NativeCoverageRf3RunProtocol.NativeScaledTimeoutProperty),
        NativeVectorTimeout = Duration(value, NativeCoverageRf3RunProtocol.NativeVectorTimeoutProperty),
        ApplicationCleanupTimeout = Duration(value,
            NativeCoverageRf3FixtureProtocol.ApplicationCleanupTimeoutProperty),
        ImageCleanupTimeout = Duration(value, NativeCoverageRf3RunProtocol.ImageCleanupTimeoutProperty),
        TerminationGrace = Duration(value, NativeCoverageRf3RunProtocol.TerminationGraceProperty),
        ProcessSettlementTimeout = Duration(value, NativeCoverageRf3RunProtocol.ProcessSettlementTimeoutProperty),
        ProcessExitPollInterval = Duration(value, NativeCoverageRf3RunProtocol.ProcessExitPollIntervalProperty),
        CleanupOutputCharacters = value.GetProperty(
            NativeCoverageRf3RunProtocol.CleanupOutputCharactersProperty).GetInt32(),
        MaximumFilterCharacters = value.GetProperty(
            NativeCoverageRf3RunProtocol.MaximumFilterCharactersProperty).GetInt32(),
        MaximumPathCharacters = value.GetProperty(
            NativeCoverageRf3FixtureProtocol.MaximumPathCharactersProperty).GetInt32()
    };

    private static TimeSpan Duration(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        if (value.ValueKind != JsonValueKind.String
            || !TimeSpan.TryParseExact(value.GetString(), NativeCoverageRf3RunProtocol.TimeSpanFormat,
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
        new(NativeCoverageRf3FixtureProtocol.InvalidRun);
}
