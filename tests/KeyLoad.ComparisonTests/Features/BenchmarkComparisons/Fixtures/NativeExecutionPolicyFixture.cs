using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

[ConfigurationBinding]
internal static class NativeExecutionPolicyFixture
{
    private const string ConfigurationFile = "native-execution.json";
    private static readonly Lazy<IOptions<NativeComparisonHarnessOptions>> harness = new(CreateHarness);

    internal static IOptions<NativeComparisonSerializationOptions> ReadSerialization()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, ConfigurationFile), optional: false)
            .AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<NativeComparisonSerializationOptions>(
            new OptionsFactory<NativeComparisonSerializationOptions>(
                [new ConfigureFromConfigurationOptions<NativeComparisonSerializationOptions>(
                    configuration.GetRequiredSection(NativeComparisonSerializationOptions.SectionName))], [],
                [new NativeComparisonSerializationOptionsValidator()]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<NativeComparisonDiagnosticOptions> ReadDiagnostics()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, ConfigurationFile), optional: false)
            .AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<NativeComparisonDiagnosticOptions>(
            new OptionsFactory<NativeComparisonDiagnosticOptions>(
                [new ConfigureFromConfigurationOptions<NativeComparisonDiagnosticOptions>(
                    configuration.GetRequiredSection(NativeComparisonDiagnosticOptions.SectionName))], [],
                [new NativeComparisonDiagnosticOptionsValidator()]));
        _ = options.Value;
        return options;
    }

    internal static IOptions<NativeComparisonHarnessOptions> Harness() => harness.Value;

    private static OptionsManager<NativeComparisonHarnessOptions> CreateHarness()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, ConfigurationFile), optional: false)
            .AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<NativeComparisonHarnessOptions>(
            new OptionsFactory<NativeComparisonHarnessOptions>(
                [new ConfigureFromConfigurationOptions<NativeComparisonHarnessOptions>(
                    configuration.GetSection(NativeComparisonHarnessOptions.SectionName))], [],
                [new NativeComparisonHarnessOptionsValidator()]));
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

    internal static IOptions<NativeComparisonExecutionOptions> Read()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, ConfigurationFile), optional: false)
            .AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<NativeComparisonExecutionOptions>(
            new OptionsFactory<NativeComparisonExecutionOptions>([new ConfigureFromConfigurationOptions<NativeComparisonExecutionOptions>(
                configuration.GetRequiredSection(NativeComparisonExecutionOptions.SectionName))], [],
                [new ValidateOptions<NativeComparisonExecutionOptions>(Options.DefaultName, value => { value.Validate(); return true; },
                    "The native comparison policy is invalid.")]));
        _ = options.Value;
        return options;
    }
}
