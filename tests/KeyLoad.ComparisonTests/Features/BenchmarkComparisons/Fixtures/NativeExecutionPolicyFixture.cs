using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class NativeExecutionPolicyFixture
{
    internal static IOptions<NativeComparisonExecutionOptions> Read()
    {
        using var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "native-execution.json"), optional: false).Build();
        return Options.Create(configuration.GetRequiredSection(NativeComparisonExecutionOptions.SectionName).Get<NativeComparisonExecutionOptions>()!.Validate());
    }
}
