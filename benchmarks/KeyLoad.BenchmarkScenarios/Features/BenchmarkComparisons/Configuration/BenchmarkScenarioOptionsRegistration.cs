using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Binds standalone native fixtures before they acquire storage ownership.</summary>
[ConfigurationBinding]
internal static class BenchmarkScenarioOptionsRegistration
{
    internal static IOptions<T> Read<T>(IConfiguration configuration, string section,
        Func<T, bool> validate, string message) where T : class, new()
    {
        var options = new OptionsManager<T>(new OptionsFactory<T>(
            [new ConfigureFromConfigurationOptions<T>(configuration.GetSection(section))], [],
            [new ValidateOptions<T>(Options.DefaultName, validate, message)]));
        _ = options.Value;
        return options;
    }
}
