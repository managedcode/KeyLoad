using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Binds and validates the required native execution policy before target construction.</summary>
[ConfigurationBinding]
internal static class NativeComparisonExecutionRegistration
{
    internal const string ConfigurationPath = "Features/BenchmarkComparisons/Configuration/native-execution.json";

    internal static IOptions<NativeComparisonExecutionOptions> Read(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddOptions<NativeComparisonExecutionOptions>()
            .Bind(configuration.GetRequiredSection(NativeComparisonExecutionOptions.SectionName));
        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IOptions<NativeComparisonExecutionOptions>>().Value.Validate();
        return Options.Create(policy);
    }
}
