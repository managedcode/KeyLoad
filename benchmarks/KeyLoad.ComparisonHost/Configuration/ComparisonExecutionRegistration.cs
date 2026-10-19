using KeyLoad.Client;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Binds and validates the required execution execution policy before target construction.</summary>
[ConfigurationBinding]
internal static class ComparisonExecutionRegistration
{
    internal const string ConfigurationPath = "Configuration/execution.json";

    internal static IOptions<ComparisonDiagnosticOptions> ReadDiagnostics(IConfiguration configuration)
    {
        var options = new OptionsManager<ComparisonDiagnosticOptions>(
            new OptionsFactory<ComparisonDiagnosticOptions>(
                [new ConfigureFromConfigurationOptions<ComparisonDiagnosticOptions>(
                    configuration.GetRequiredSection(ComparisonDiagnosticOptions.SectionName))], [],
                [new ComparisonDiagnosticOptionsValidator()]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<ComparisonExecutionOptions> Read(IConfiguration configuration)
    {
        var options = new OptionsManager<ComparisonExecutionOptions>(
            new OptionsFactory<ComparisonExecutionOptions>([new ConfigureFromConfigurationOptions<ComparisonExecutionOptions>(
                configuration.GetRequiredSection(ComparisonExecutionOptions.SectionName))], [],
                [new ValidateOptions<ComparisonExecutionOptions>(Options.DefaultName,
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
        var options = new OptionsManager<ComparisonHostExecutionOptions>(
            new OptionsFactory<ComparisonHostExecutionOptions>([new ConfigureFromConfigurationOptions<ComparisonHostExecutionOptions>(
                configuration.GetSection(ComparisonHostExecutionOptions.SectionName))], [],
                [new ValidateOptions<ComparisonHostExecutionOptions>(Options.DefaultName,
                    value => value.IsValid(), ComparisonHostExecutionOptions.InvalidSettings)]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<IsolatedKeyLoadAdmissionOptions> ReadIsolatedAdmission(IConfiguration configuration)
    {
        var options = new OptionsManager<IsolatedKeyLoadAdmissionOptions>(new OptionsFactory<IsolatedKeyLoadAdmissionOptions>(
            [new ConfigureFromConfigurationOptions<IsolatedKeyLoadAdmissionOptions>(
                configuration.GetSection(IsolatedKeyLoadAdmissionOptions.SectionName))], [],
            [new ValidateOptions<IsolatedKeyLoadAdmissionOptions>(Options.DefaultName,
                value => value.IsValid(), IsolatedKeyLoadAdmissionOptions.ValidationMessage)]));
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
