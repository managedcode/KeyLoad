using KeyLoad.ComparisonHost.BenchmarkComparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

[ConfigurationBinding]
internal static class ExecutionPolicyFixture
{
    private static readonly Lazy<IOptions<ComparisonHarnessOptions>> harness = new(CreateHarness);

    internal static IOptions<ComparisonHarnessOptions> Harness() => harness.Value;

    private static OptionsManager<ComparisonHarnessOptions> CreateHarness()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, ComparisonExecutionRegistration.ConfigurationPath), optional: false)
            .AddEnvironmentVariables();
        var options = new OptionsManager<ComparisonHarnessOptions>(new HarnessOptionsFactory(configuration));
        _ = options.Value;
        return options;
    }

    private sealed class HarnessOptionsFactory(IConfiguration configuration) : OptionsFactory<ComparisonHarnessOptions>(
        [new ConfigureFromConfigurationOptions<ComparisonHarnessOptions>(configuration.GetSection(ComparisonHarnessOptions.SectionName))],
        [], [new ComparisonHarnessOptionsValidator()])
    {
        protected override ComparisonHarnessOptions CreateInstance(string name) => new();
    }
}
