using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

internal static class UnitBenchmarkOptions
{
    private const string SolutionFile = "KeyLoad.slnx";
    private const string NativePolicyFile = "benchmarks/KeyLoad.ComparisonHost/Features/BenchmarkComparisons/Configuration/native-execution.json";
    private const string MissingCheckout = "The native comparison policy requires the actual KeyLoad checkout.";

    internal static IOptions<NativeComparisonDiagnosticOptions> Diagnostics()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, SolutionFile)))
        { root = root.Parent; }
        if (root is null)
        { throw new DirectoryNotFoundException(MissingCheckout); }
        var configuration = new ConfigurationBuilder().SetBasePath(root.FullName)
            .AddJsonFile(NativePolicyFile, optional: false).Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<NativeComparisonDiagnosticOptions>(
            new OptionsFactory<NativeComparisonDiagnosticOptions>(
                [new ConfigureFromConfigurationOptions<NativeComparisonDiagnosticOptions>(
                    configuration.GetRequiredSection(NativeComparisonDiagnosticOptions.SectionName))], [],
                [new NativeComparisonDiagnosticOptionsValidator()]));
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

    internal static IOptions<NativeComparisonExecutionOptions> Native()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "KeyLoad.slnx")))
        { root = root.Parent; }
        if (root is null)
        { throw new DirectoryNotFoundException("The native comparison policy requires the actual KeyLoad checkout."); }
        var configuration = new ConfigurationBuilder().SetBasePath(root.FullName)
            .AddJsonFile("benchmarks/KeyLoad.ComparisonHost/Features/BenchmarkComparisons/Configuration/native-execution.json", optional: false).Build();
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
