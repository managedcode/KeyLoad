using KeyLoad.Comparisons;
using KeyLoad.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Binds and validates the required native execution policy before target construction.</summary>
[ConfigurationBinding]
internal static class NativeComparisonExecutionRegistration
{
    internal const string ConfigurationPath = "Features/BenchmarkComparisons/Configuration/native-execution.json";

    internal static IOptions<NativeComparisonExecutionOptions> Read(IConfiguration configuration)
    {
        IOptions<NativeComparisonExecutionOptions> options = new OptionsManager<NativeComparisonExecutionOptions>(
            new OptionsFactory<NativeComparisonExecutionOptions>([new ConfigureFromConfigurationOptions<NativeComparisonExecutionOptions>(
                configuration.GetRequiredSection(NativeComparisonExecutionOptions.SectionName))], [],
                [new ValidateOptions<NativeComparisonExecutionOptions>(Options.DefaultName,
                    value => { value.Validate(); return true; }, ComparisonHostConstants.MissingSettingPrefix)]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<ComparisonLifecycleOptions> ReadLifecycle(IConfiguration configuration)
    {
        var options = new OptionsManager<ComparisonLifecycleOptions>(new OptionsFactory<ComparisonLifecycleOptions>(
            [new ConfigureFromConfigurationOptions<ComparisonLifecycleOptions>(
                configuration.GetSection(ComparisonLifecycleOptions.SectionName))], [],
            [new ValidateOptions<ComparisonLifecycleOptions>(Options.DefaultName,
                settings => settings.IsValid(), ComparisonLifecycleOptions.ValidationMessage)]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<ComparisonHostExecutionOptions> ReadHost(IConfiguration configuration)
    {
        IOptions<ComparisonHostExecutionOptions> options = new OptionsManager<ComparisonHostExecutionOptions>(
            new OptionsFactory<ComparisonHostExecutionOptions>([new ConfigureFromConfigurationOptions<ComparisonHostExecutionOptions>(
                configuration.GetSection(ComparisonHostExecutionOptions.SectionName))], [],
                [new ValidateOptions<ComparisonHostExecutionOptions>(Options.DefaultName,
                    value => value.IsValid(), ComparisonHostExecutionOptions.InvalidSettings)]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<KeyLoadClientExecutionOptions> ReadClient(IConfiguration configuration)
    {
        var options = new OptionsManager<KeyLoadClientExecutionOptions>(new OptionsFactory<KeyLoadClientExecutionOptions>(
            [new ConfigureFromConfigurationOptions<KeyLoadClientExecutionOptions>(
                configuration.GetSection(KeyLoadClientExecutionOptions.SectionName))], [],
            [new ValidateOptions<KeyLoadClientExecutionOptions>(Options.DefaultName,
                settings => settings.IsValid(), KeyLoadClientExecutionOptions.ValidationMessage)]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<QueryTranslationOptions> ReadTranslation(IConfiguration configuration)
    {
        var options = new OptionsManager<QueryTranslationOptions>(new OptionsFactory<QueryTranslationOptions>(
            [new ConfigureFromConfigurationOptions<QueryTranslationOptions>(
                configuration.GetSection(QueryTranslationOptions.SectionName))], [],
            [new ValidateOptions<QueryTranslationOptions>(Options.DefaultName,
                settings => settings.IsValid(), QueryTranslationOptions.ValidationMessage)]));
        _ = options.Value;
        return options;
    }
}
