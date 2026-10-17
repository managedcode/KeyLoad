using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

[ConfigurationBinding]
internal static class NativeCoverageRf3FixtureOptions
{
    internal const string GithubShaEnvironment = "GITHUB_SHA";
    private static readonly string[] RequiredProperties =
    [
        nameof(NativeCoverageExecutionOptions.MaximumDescriptorBytes),
        nameof(NativeCoverageExecutionOptions.MaximumFiles),
        nameof(NativeCoverageExecutionOptions.ReadBufferBytes),
        nameof(NativeCoverageExecutionOptions.MaximumTotalBytes),
        nameof(NativeCoverageExecutionOptions.MaximumFileBytes),
        nameof(NativeCoverageExecutionOptions.MaximumPathCharacters),
        nameof(NativeCoverageExecutionOptions.MaximumManifestBytes),
        nameof(NativeCoverageExecutionOptions.MaximumReportBytes),
        nameof(NativeCoverageExecutionOptions.ShutdownTimeout),
        nameof(NativeCoverageExecutionOptions.SettlementTimeout),
        nameof(NativeCoverageExecutionOptions.ContainerStopTimeout),
        nameof(NativeCoverageExecutionOptions.ApplicationCleanupTimeout)
    ];

    internal static IOptions<NativeCoverageExecutionOptions>? Read()
    {
        var mode = OptionalEnvironment(NativeCoverageRf3FixtureProtocol.ModeEnvironment);
        var sourcePath = OptionalEnvironment(NativeCoverageRf3FixtureProtocol.SourceManifestEnvironment);
        var runPath = OptionalEnvironment(NativeCoverageRf3FixtureProtocol.RunManifestEnvironment);
        var runId = OptionalEnvironment(NativeCoverageRf3FixtureProtocol.RunIdEnvironment);
        var image = OptionalEnvironment(NativeCoverageRf3FixtureProtocol.ImageReferenceEnvironment);
        if (mode is null && sourcePath is null && runPath is null && runId is null && image is null)
        {
            return null;
        }
        if (mode != NativeCoverageRf3FixtureProtocol.Mode)
        {
            throw Invalid();
        }
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var lifetime = configuration as IDisposable;
        var section = configuration.GetSection(NativeCoverageExecutionOptions.SectionName);
        foreach (var property in RequiredProperties)
        {
            if (string.IsNullOrWhiteSpace(section[property]))
            {
                throw Invalid();
            }
        }
        return AppHostOptionsRegistration.BindNativeCoverage(configuration);
    }

    internal static string? OptionalEnvironment(string name) => Environment.GetEnvironmentVariable(name);

    internal static string RequiredEnvironment(string name) => OptionalEnvironment(name) ?? throw Invalid();

    private static InvalidOperationException Invalid() => new(NativeCoverageRf3FixtureProtocol.InvalidContext);
}
