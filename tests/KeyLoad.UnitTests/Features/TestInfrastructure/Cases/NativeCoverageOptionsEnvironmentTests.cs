using System.Globalization;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class NativeCoverageOptionsEnvironmentTests
{
    private const string EnvironmentPrefix = "KeyLoadTests__NativeCoverage__";
    private const string SourceManifestPath = "/coverage/source-manifest.json";
    private const string FixtureModeEnvironment = "KEYLOAD_NATIVE_COVERAGE_RF3_MODE";
    private const string FixtureSourceEnvironment = "KEYLOAD_NATIVE_COVERAGE_SOURCE_MANIFEST";
    private const string ConfigurationSeparator = ":";
    private const string EnvironmentSeparator = "__";

    [Test]
    public async Task NativeAspireEnvironmentTransportsTheCompleteValidatedConfiguredSnapshot()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(ConfiguredValues());
        var original = AppHostOptionsRegistration.BindNativeCoverage(configuration);
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        { DisableDashboard = true, Args = [] });
        var runner = builder.AddExecutable("coverage-options-handoff", "dotnet", ".");
        NativeCoverageRf3ExecutionEnvironment.Apply(runner, original, SourceManifestPath);
        var environment = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, runner.Resource, environment);
        foreach (var callback in runner.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }
        await Assert.That(environment[FixtureModeEnvironment]).IsEqualTo(NativeCoverageRf3Protocol.Mode);
        await Assert.That(environment[FixtureSourceEnvironment]).IsEqualTo(SourceManifestPath);
        await Assert.That(environment.Keys.Count(key => key.StartsWith(EnvironmentPrefix, StringComparison.Ordinal))).IsEqualTo(12);
        using var received = new ConfigurationManager();
        received.AddInMemoryCollection(environment.Where(pair => pair.Key.StartsWith(EnvironmentPrefix, StringComparison.Ordinal))
            .Select(pair => new KeyValuePair<string, string?>(
                pair.Key.Replace(EnvironmentSeparator, ConfigurationSeparator, StringComparison.Ordinal),
                Convert.ToString(pair.Value, CultureInfo.InvariantCulture))));
        var rebound = AppHostOptionsRegistration.BindNativeCoverage(received);
        await Assert.That(JsonSerializer.Serialize(rebound.Value)).IsEqualTo(JsonSerializer.Serialize(original.Value));
        await Assert.That(rebound.Value.ReadBufferBytes).IsEqualTo(4096);
        await Assert.That(rebound.Value.ContainerStopTimeout).IsEqualTo(TimeSpan.FromSeconds(25));
    }

    private static Dictionary<string, string?> ConfiguredValues()
    {
        var prefix = NativeCoverageExecutionOptions.SectionName + ConfigurationSeparator;
        return new Dictionary<string, string?>
        {
            [prefix + nameof(NativeCoverageExecutionOptions.MaximumDescriptorBytes)] = "32768",
            [prefix + nameof(NativeCoverageExecutionOptions.MaximumFiles)] = "2048",
            [prefix + nameof(NativeCoverageExecutionOptions.ReadBufferBytes)] = "4096",
            [prefix + nameof(NativeCoverageExecutionOptions.MaximumTotalBytes)] = "1073741824",
            [prefix + nameof(NativeCoverageExecutionOptions.MaximumFileBytes)] = "134217728",
            [prefix + nameof(NativeCoverageExecutionOptions.MaximumPathCharacters)] = "512",
            [prefix + nameof(NativeCoverageExecutionOptions.MaximumManifestBytes)] = "8388608",
            [prefix + nameof(NativeCoverageExecutionOptions.MaximumReportBytes)] = "67108864",
            [prefix + nameof(NativeCoverageExecutionOptions.ShutdownTimeout)] = "00:00:08",
            [prefix + nameof(NativeCoverageExecutionOptions.SettlementTimeout)] = "00:00:12",
            [prefix + nameof(NativeCoverageExecutionOptions.ContainerStopTimeout)] = "00:00:25",
            [prefix + nameof(NativeCoverageExecutionOptions.ApplicationCleanupTimeout)] = "00:01:20"
        };
    }
}
