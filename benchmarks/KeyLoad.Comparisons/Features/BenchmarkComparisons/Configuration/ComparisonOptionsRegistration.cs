using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Creates the actual standalone workload snapshot through native binding and validation.</summary>
[ConfigurationBinding]
public static class ComparisonOptionsRegistration
{
    /// <summary>Binds the established benchmark section without allocating execution resources.</summary>
    /// <param name="configuration">The native host configuration with established precedence.</param>
    /// <returns>The same validated native snapshot used throughout a comparison operation.</returns>
    public static IOptions<ComparisonOptions> Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = new OptionsManager<ComparisonOptions>(new OptionsFactory<ComparisonOptions>(
            [new ConfigureFromConfigurationOptions<ComparisonOptions>(configuration.GetSection(ComparisonOptions.SectionName))], [],
            [new ValidateOptions<ComparisonOptions>(Options.DefaultName,
                settings => settings.IsValid(), ComparisonOptions.ValidationMessage)]));
        _ = options.Value;
        return options;
    }
}
