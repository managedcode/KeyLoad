using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Binds the canonical open-loop policy before admitting a native target.</summary>
[ConfigurationBinding]
internal static class OpenLoopExecutionRegistration
{
    internal static IOptions<OpenLoopExecutionOptions> Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<OpenLoopExecutionOptions>, OpenLoopExecutionOptionsValidator>();
        services.AddOptions<OpenLoopExecutionOptions>()
            .Bind(configuration.GetSection(OpenLoopExecutionOptions.SectionName)).ValidateOnStart();
        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IOptions<OpenLoopExecutionOptions>>().Value;
        return Options.Create(policy);
    }
}
