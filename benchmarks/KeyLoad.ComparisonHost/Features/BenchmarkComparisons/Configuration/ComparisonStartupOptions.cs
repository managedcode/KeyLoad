using KeyLoad;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

[ConfigurationOptions]
internal sealed class ComparisonStartupOptions
{
    [ConfigurationKeyName(ComparisonWorkerSelection.TargetSetting)] public string? Target { get; set; }
    [ConfigurationKeyName(ComparisonHostConstants.Profile)] public string? Profile { get; set; }
}

[ConfigurationBinding]
internal static class ComparisonStartupRegistration
{
    internal static IOptions<ComparisonStartupOptions> Read(IConfiguration configuration)
    {
        IOptions<ComparisonStartupOptions> options = new OptionsManager<ComparisonStartupOptions>(
            new OptionsFactory<ComparisonStartupOptions>([new ConfigureFromConfigurationOptions<ComparisonStartupOptions>(configuration)], [], []));
        _ = options.Value;
        return options;
    }
}
