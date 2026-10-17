using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure.Validation;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

[ConfigurationBinding]
internal sealed record TestSuiteSettings(
    string Suite,
    string Project,
    string? Filter,
    TimeSpan Timeout,
    int MaximumParallelTests,
    string? ResultsDirectory,
    bool ReportTrx,
    string? CoverageSettings,
    string? CoverageOutput,
    bool LocalRf3ImageEnabled)
{
    private const string GetResourceNameComparisonText = "tests-";

    internal NativeCoverageRf3Selection? NativeCoverageRf3
    {
        get; init;
    }
    internal string CoverageFormat { get; init; } = NativeCoverageProtocol.CoberturaFormat;
    internal const string SuiteSetting = TestSuiteProtocol.SuiteSetting;
    internal const string SuiteEnvironment = TestSuiteProtocol.SuiteEnvironment;
    internal const string FilterSetting = TestSuiteProtocol.FilterSetting;
    internal const string TimeoutSetting = TestSuiteProtocol.TimeoutSetting;

    internal string ResourceName => GetResourceNameComparisonText + Suite;

    internal static bool Requested(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        TestSuiteSelectionValidator.RejectRetiredBenchmarkArguments(args);
        TestSuiteSelectionValidator.RejectRetiredBenchmarkSelection(configuration);
        return Requested(args, AppHostOptionsRegistration.BindTestBootstrap(configuration));
    }

    internal static bool Requested(string[] args, IOptions<TestBootstrapOptions> options)
    {
        var selectors = options.Value;
        return TestSuiteSelectionValidator.Requested(args, selectors.Suite);
    }

    internal static TestSuiteSettings? Read(IConfiguration configuration)
        => Read(configuration, AppHostOptionsRegistration.BindTestExecution(configuration));

    internal static TestSuiteSettings? Read(IConfiguration configuration, IOptions<TestExecutionOptions> execution)
        => Read(configuration, execution, AppHostOptionsRegistration.BindNativeCoverage(configuration));

    internal static TestSuiteSettings? Read(IConfiguration configuration, IOptions<TestExecutionOptions> execution,
        IOptions<NativeCoverageExecutionOptions> coverage)
        => TestSuiteSettingsReader.Read(configuration, execution, coverage);
}
