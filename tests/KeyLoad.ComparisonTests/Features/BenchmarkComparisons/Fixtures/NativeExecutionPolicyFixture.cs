using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class NativeExecutionPolicyFixture
{
    internal static IOptions<NativeComparisonExecutionOptions> Read()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "native-execution.json"), optional: false).Build();
        using var configurationLifetime = configuration as IDisposable;
        IOptions<NativeComparisonExecutionOptions> options = new OptionsManager<NativeComparisonExecutionOptions>(
            new OptionsFactory<NativeComparisonExecutionOptions>([new ConfigureFromConfigurationOptions<NativeComparisonExecutionOptions>(
                configuration.GetRequiredSection(NativeComparisonExecutionOptions.SectionName))], [],
                [new ValidateOptions<NativeComparisonExecutionOptions>(Options.DefaultName, value => { value.Validate(); return true; },
                    "The native comparison policy is invalid.")]));
        _ = options.Value;
        return options;
    }
}
