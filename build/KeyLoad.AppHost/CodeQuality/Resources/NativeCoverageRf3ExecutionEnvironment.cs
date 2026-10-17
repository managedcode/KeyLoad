using System.Globalization;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal static class NativeCoverageRf3ExecutionEnvironment
{
    private const string EnvironmentPrefix = "KeyLoadTests__NativeCoverage__";
    private const string DurationFormat = "c";
    private const string FixtureModeEnvironment = "KEYLOAD_NATIVE_COVERAGE_RF3_MODE";
    private const string FixtureSourceEnvironment = "KEYLOAD_NATIVE_COVERAGE_SOURCE_MANIFEST";

    internal static void Apply(IResourceBuilder<ExecutableResource> runner,
        IOptions<NativeCoverageExecutionOptions> options, string sourceManifestPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        var policy = options.Value;
        if (!policy.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageExecutionOptions.ValidationMessage);
        }
        runner.WithEnvironment(FixtureModeEnvironment, NativeCoverageRf3Protocol.Mode)
            .WithEnvironment(FixtureSourceEnvironment, sourceManifestPath)
            .WithEnvironment(Key(nameof(policy.MaximumDescriptorBytes)), Number(policy.MaximumDescriptorBytes))
            .WithEnvironment(Key(nameof(policy.MaximumFiles)), Number(policy.MaximumFiles))
            .WithEnvironment(Key(nameof(policy.ReadBufferBytes)), Number(policy.ReadBufferBytes))
            .WithEnvironment(Key(nameof(policy.MaximumTotalBytes)), Number(policy.MaximumTotalBytes))
            .WithEnvironment(Key(nameof(policy.MaximumFileBytes)), Number(policy.MaximumFileBytes))
            .WithEnvironment(Key(nameof(policy.MaximumPathCharacters)), Number(policy.MaximumPathCharacters))
            .WithEnvironment(Key(nameof(policy.MaximumManifestBytes)), Number(policy.MaximumManifestBytes))
            .WithEnvironment(Key(nameof(policy.MaximumReportBytes)), Number(policy.MaximumReportBytes))
            .WithEnvironment(Key(nameof(policy.ShutdownTimeout)), Duration(policy.ShutdownTimeout))
            .WithEnvironment(Key(nameof(policy.SettlementTimeout)), Duration(policy.SettlementTimeout))
            .WithEnvironment(Key(nameof(policy.ContainerStopTimeout)), Duration(policy.ContainerStopTimeout))
            .WithEnvironment(Key(nameof(policy.ApplicationCleanupTimeout)), Duration(policy.ApplicationCleanupTimeout));
    }

    private static string Key(string property) => EnvironmentPrefix + property;
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Duration(TimeSpan value) => value.ToString(DurationFormat, CultureInfo.InvariantCulture);
}
