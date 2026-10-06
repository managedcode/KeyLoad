using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure.Validation;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

[ConfigurationBinding]
internal sealed record TestSuiteSettings(
    string Suite,
    string Project,
    string? Filter,
    TimeSpan Timeout,
    string? ResultsDirectory,
    bool ReportTrx,
    string? CoverageSettings,
    string? CoverageOutput,
    string? ComparisonTarget,
    bool LocalRf3ImageEnabled,
    ScaledComparisonProfile? ScaleProfile)
{
    private const string GetResourceNameComparisonText = "tests-";

    internal VectorComparisonProfile? VectorProfile { get; init; }
    internal int? OpenLoopRate { get; init; }
    internal NativeCoverageRf3Selection? NativeCoverageRf3 { get; init; }
    internal string CoverageFormat { get; init; } = NativeCoverageProtocol.CoberturaFormat;
    internal const string VectorProfileSetting = TestSuiteProtocol.VectorProfileSetting;
    internal const string VectorProfileEnvironment = TestSuiteProtocol.VectorProfileEnvironment;
    internal const string SuiteSetting = TestSuiteProtocol.SuiteSetting;
    internal const string SuiteEnvironment = TestSuiteProtocol.SuiteEnvironment;
    internal const string ScaleProfileSetting = TestSuiteProtocol.ScaleProfileSetting;
    internal const string ScaleProfileEnvironment = TestSuiteProtocol.ScaleProfileEnvironment;
    internal const string BenchmarkEnabledSetting = TestSuiteProtocol.BenchmarkEnabledSetting;
    internal const string FilterSetting = TestSuiteProtocol.FilterSetting;
    internal const string TimeoutSetting = TestSuiteProtocol.TimeoutSetting;

    internal string ResourceName => GetResourceNameComparisonText + Suite;

    internal static bool Requested(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        return Requested(args, AppHostOptionsRegistration.BindTestBootstrap(configuration));
    }

    internal static bool Requested(string[] args, IOptions<TestBootstrapOptions> options)
    {
        var selectors = options.Value;
        return TestSuiteSelectionValidator.Requested(args, selectors.Suite, selectors.VectorProfile,
            selectors.ScaleProfile, selectors.OpenLoopRate);
    }

    internal static TestSuiteSettings? Read(IConfiguration configuration)
        => Read(configuration, AppHostOptionsRegistration.BindTestExecution(configuration));

    internal static TestSuiteSettings? Read(IConfiguration configuration, IOptions<TestExecutionOptions> execution)
        => Read(configuration, execution, AppHostOptionsRegistration.BindNativeCoverage(configuration));

    internal static TestSuiteSettings? Read(IConfiguration configuration, IOptions<TestExecutionOptions> execution,
        IOptions<NativeCoverageExecutionOptions> coverage)
        => TestSuiteSettingsReader.Read(configuration, execution, coverage);
}
