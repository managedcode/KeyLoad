using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests;

internal static class UnitBenchmarkOptions
{
    private const string SolutionFile = "KeyLoad.slnx";
    private const string PolicyFile = "benchmarks/KeyLoad.ComparisonHost/Configuration/execution.json";
    private const string MissingCheckout = "The execution comparison policy requires the actual KeyLoad checkout.";

    internal static IOptions<ComparisonDiagnosticOptions> Diagnostics()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, SolutionFile)))
        {
            root = root.Parent;
        }
        if (root is null)
        {
            throw new DirectoryNotFoundException(MissingCheckout);
        }
        var configuration = new ConfigurationBuilder().SetBasePath(root.FullName)
            .AddJsonFile(PolicyFile, optional: false).Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<ComparisonDiagnosticOptions>(
            new OptionsFactory<ComparisonDiagnosticOptions>(
                [new ConfigureFromConfigurationOptions<ComparisonDiagnosticOptions>(
                    configuration.GetRequiredSection(ComparisonDiagnosticOptions.SectionName))], [],
                [new ComparisonDiagnosticOptionsValidator()]));
        _ = options.Value;
        return options;
    }
    internal static IOptions<ComparisonLifecycleOptions> Lifecycle(ComparisonLifecycleOptions? configured = null)
    {
        var value = configured ?? new ComparisonLifecycleOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<RawStorageExecutionOptions> RawStorage()
    {
        var value = new RawStorageExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }
    internal static IOptions<ScaledStorageExecutionOptions> ScaledPreparation { get; } = ScaledStorage();

    internal static IOptions<ScaledStorageExecutionOptions> ScaledStorage()
    {
        var value = new ScaledStorageExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<IsolatedKeyLoadAdmissionOptions> KeyLoadAdmission()
    {
        var value = new IsolatedKeyLoadAdmissionOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<ComparisonExecutionOptions> Execution()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "KeyLoad.slnx")))
        {
            root = root.Parent;
        }
        if (root is null)
        {
            throw new DirectoryNotFoundException("The execution comparison policy requires the actual KeyLoad checkout.");
        }
        var configuration = new ConfigurationBuilder().SetBasePath(root.FullName)
            .AddJsonFile("benchmarks/KeyLoad.ComparisonHost/Configuration/execution.json", optional: false).Build();
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
