using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

[ConfigurationBinding]
internal static class ExecutionPolicyFixture
{
    private const string ConfigurationFile = "execution.json";
    private static readonly Lazy<IOptions<ComparisonHarnessOptions>> harness = new(CreateHarness);

    internal static IOptions<ComparisonSerializationOptions> ReadSerialization()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, ConfigurationFile), optional: false)
            .AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<ComparisonSerializationOptions>(
            new OptionsFactory<ComparisonSerializationOptions>(
                [new ConfigureFromConfigurationOptions<ComparisonSerializationOptions>(
                    configuration.GetRequiredSection(ComparisonSerializationOptions.SectionName))], [],
                [new ComparisonSerializationOptionsValidator()]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<ComparisonDiagnosticOptions> ReadDiagnostics()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, ConfigurationFile), optional: false)
            .AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<ComparisonDiagnosticOptions>(
            new OptionsFactory<ComparisonDiagnosticOptions>(
                [new ConfigureFromConfigurationOptions<ComparisonDiagnosticOptions>(
                    configuration.GetRequiredSection(ComparisonDiagnosticOptions.SectionName))], [],
                [new ComparisonDiagnosticOptionsValidator()]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<ComparisonHarnessOptions> Harness() => harness.Value;

    private static OptionsManager<ComparisonHarnessOptions> CreateHarness()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, ConfigurationFile), optional: false)
            .AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<ComparisonHarnessOptions>(
            new OptionsFactory<ComparisonHarnessOptions>(
                [new ConfigureFromConfigurationOptions<ComparisonHarnessOptions>(
                    configuration.GetSection(ComparisonHarnessOptions.SectionName))], [],
                [new ComparisonHarnessOptionsValidator()]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<IsolatedKeyLoadAdmissionOptions> Admission(IsolatedKeyLoadAdmissionOptions? configured = null)
    {
        var value = configured ?? new IsolatedKeyLoadAdmissionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<HttpAdmissionLimits> Http(HttpAdmissionLimits? configured = null)
    {
        var value = configured ?? new HttpAdmissionLimits();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<ComparisonLifecycleOptions> Lifecycle()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, ConfigurationFile), optional: false)
            .AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<ComparisonLifecycleOptions>(
            new OptionsFactory<ComparisonLifecycleOptions>(
                [new ConfigureFromConfigurationOptions<ComparisonLifecycleOptions>(
                    configuration.GetSection(ComparisonLifecycleOptions.SectionName))], [],
                [new ValidateOptions<ComparisonLifecycleOptions>(Options.DefaultName, value => value.IsValid(),
                    ComparisonLifecycleOptions.ValidationMessage)]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<ComparisonOptions> Workload(ComparisonOptions value)
    {
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<ComparisonExecutionOptions> Read()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, ConfigurationFile), optional: false)
            .AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<ComparisonExecutionOptions>(
            new OptionsFactory<ComparisonExecutionOptions>([new ConfigureFromConfigurationOptions<ComparisonExecutionOptions>(
                configuration.GetRequiredSection(ComparisonExecutionOptions.SectionName))], [],
                [new ValidateOptions<ComparisonExecutionOptions>(Options.DefaultName, value => { value.Validate(); return true; },
                    "The execution comparison policy is invalid.")]));
        _ = options.Value;
        return options;
    }
}
