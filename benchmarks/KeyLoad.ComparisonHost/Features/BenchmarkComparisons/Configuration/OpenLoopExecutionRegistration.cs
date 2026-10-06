using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Binds the canonical open-loop policy before admitting a native target.</summary>
[ConfigurationBinding]
internal static class OpenLoopExecutionRegistration
{
    internal static IOptions<OpenLoopExecutionOptions> Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = new OptionsManager<OpenLoopExecutionOptions>(
            new OptionsFactory<OpenLoopExecutionOptions>([new ConfigureFromConfigurationOptions<OpenLoopExecutionOptions>(
                configuration.GetSection(OpenLoopExecutionOptions.SectionName))], [], [new OpenLoopExecutionOptionsValidator()]));
        _ = options.Value;
        return options;
    }
}
