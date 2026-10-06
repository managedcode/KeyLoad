using KeyLoad;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Options;
using KeyLoad.AppHost.Features.TestInfrastructure.Validation;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

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
    private const string ConflictingTestProfilesMessage = "Scale and vector test profiles cannot be combined.";
    private const string ConflictingExecutionModesMessage = "Test and benchmark modes cannot be combined.";
    private const string InvalidTestFilterMessage = "The test filter is invalid.";
    private const string InvalidResultsDirectoryMessage = "The results directory is invalid.";
    private const string InvalidCoverageSettingsPathMessage = "The coverage settings path is invalid.";
    private const string InvalidCoverageOutputPathMessage = "The coverage output path is invalid.";
    private const string IncompleteCoverageSettingsMessage = "Coverage settings and output must be configured together.";

    private const string GetResourceNameComparisonText = "tests-";

    internal VectorComparisonProfile? VectorProfile { get; init; }
    internal int? OpenLoopRate { get; init; }
    internal const string VectorProfileSetting = TestSuiteProtocol.VectorProfileSetting;
    internal const string VectorProfileEnvironment = TestSuiteProtocol.VectorProfileEnvironment;
    internal const string SuiteSetting = TestSuiteProtocol.SuiteSetting;
    internal const string SuiteEnvironment = TestSuiteProtocol.SuiteEnvironment;
    internal const string ScaleProfileSetting = TestSuiteProtocol.ScaleProfileSetting;
    internal const string ScaleProfileEnvironment = TestSuiteProtocol.ScaleProfileEnvironment;
    internal const string BenchmarkEnabledSetting = TestSuiteProtocol.BenchmarkEnabledSetting;
    internal const string FilterSetting = TestSuiteProtocol.FilterSetting;
    internal const string TimeoutSetting = TestSuiteProtocol.TimeoutSetting;
    private const string ResultsDirectorySetting = "KeyLoadTests:ResultsDirectory";
    private const string ReportTrxSetting = "KeyLoadTests:ReportTrx";
    private const string CoverageSettingsSetting = "KeyLoadTests:CoverageSettings";
    private const string CoverageOutputSetting = "KeyLoadTests:CoverageOutput";
    private static readonly string[] WorkloadOverrideNames =
        [TestSuiteProtocol.DocumentsWorkloadSettingName, TestSuiteProtocol.OperationsWorkloadSettingName,
            TestSuiteProtocol.WarmupWorkloadSettingName, TestSuiteProtocol.RepetitionsWorkloadSettingName,
            TestSuiteProtocol.ConcurrencyWorkloadSettingName, TestSuiteProtocol.PayloadBytesWorkloadSettingName,
            TestSuiteProtocol.SeedWorkloadSettingName, TestSuiteProtocol.DimensionsWorkloadSettingName,
            TestSuiteProtocol.TopKWorkloadSettingName, TestSuiteProtocol.TimeoutSecondsWorkloadSettingName,
            TestSuiteProtocol.GraphVerticesWorkloadSettingName, TestSuiteProtocol.GraphFanOutWorkloadSettingName,
            TestSuiteProtocol.GraphDepthWorkloadSettingName];

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
    {
        var suite = configuration[SuiteSetting];
        var filter = configuration[FilterSetting];
        var openLoopRate = TestSuiteSelectionValidator.ReadOpenLoopRate(
            configuration[TestSuiteSelectionValidator.OpenLoopRateSetting],
            configuration[ComparisonWorkerSelection.OpenLoopRateSetting], suite, filter,
            configuration[TestSuiteSelectionValidator.OpenLoopCancellationProofSetting] is not null);
        if (string.IsNullOrWhiteSpace(suite))
        {
            return ReadWithoutSuite(configuration, suite, filter);
        }
        return ReadSelectedSuite(configuration, execution.Value, suite, filter, openLoopRate);
    }

    private static TestSuiteSettings? ReadWithoutSuite(IConfiguration configuration, string? suite, string? filter)
    {
        const string InvalidScaleProfile = "The scale-profile test selection is invalid.";
        const string InvalidVectorProfile = "The vector-profile test selection is invalid.";
        _ = LocalRf3ImageRequest.ReadEnabled(configuration, suite, filter);
        if (!string.IsNullOrEmpty(configuration[ScaleProfileSetting]))
        {
            throw new InvalidOperationException(InvalidScaleProfile);
        }
        if (!string.IsNullOrEmpty(configuration[VectorProfileSetting]))
        {
            throw new InvalidOperationException(InvalidVectorProfile);
        }
        return null;
    }

    private static TestSuiteSettings ReadSelectedSuite(IConfiguration configuration, TestExecutionOptions execution,
        string suite, string? filter, int? openLoopRate)
    {
        var project = TestSuiteSelectionValidator.ProjectForSuite(suite);
        var comparisonTarget = configuration[ComparisonWorkerSelection.TargetSetting];
        var workloadOverrides = HasWorkloadOverride(configuration);
        var expectedFilter = TestSuiteSelectionValidator.ExpectedScaleFilter(openLoopRate, filter);
        var scaleProfile = TestSuiteSelectionValidator.ReadScaleProfile(configuration, suite, comparisonTarget,
            expectedFilter, workloadOverrides);
        var vectorProfile = VectorTestSuiteSelection.Read(configuration, suite, comparisonTarget, workloadOverrides);
        TestSuiteSelectionValidator.ValidateOpenLoop(openLoopRate, suite, comparisonTarget, filter, scaleProfile,
            vectorProfile is not null,
            configuration[VectorProfileSetting] is not null
                || configuration[ComparisonWorkerSelection.VectorProfileSetting] is not null,
            workloadOverrides, configuration[TestSuiteProtocol.AppHostBenchmarkProfileSetting],
            configuration[ComparisonWorkerSelection.NodeCountSetting],
            configuration[ComparisonWorkerSelection.ScenarioSetting]);
        if (scaleProfile is not null && vectorProfile is not null)
        {
            throw new InvalidOperationException(ConflictingTestProfilesMessage);
        }
        if (configuration.GetValue<bool>(BenchmarkEnabledSetting)
            || comparisonTarget is not null && suite != TestSuiteProtocol.ComparisonSuite)
        {
            throw new InvalidOperationException(ConflictingExecutionModesMessage);
        }
        ValidateBoundedValue(filter, execution.MaximumFilterCharacters,
            InvalidTestFilterMessage, allowBlank: true);
        var localRf3ImageEnabled = LocalRf3ImageRequest.ReadEnabled(configuration, suite, filter);
        if (localRf3ImageEnabled && (scaleProfile is not null || vectorProfile is not null))
        {
            throw new InvalidOperationException(ConflictingExecutionModesMessage);
        }
        return CreateSettings(configuration, execution, suite, project, filter, comparisonTarget,
            localRf3ImageEnabled, scaleProfile, vectorProfile, openLoopRate);
    }

    private static TestSuiteSettings CreateSettings(IConfiguration configuration, TestExecutionOptions execution,
        string suite, string project, string? filter, string? comparisonTarget, bool localRf3ImageEnabled,
        ScaledComparisonProfile? scaleProfile, VectorComparisonProfile? vectorProfile, int? openLoopRate)
    {
        var resultsDirectory = configuration[ResultsDirectorySetting];
        var coverageSettings = configuration[CoverageSettingsSetting];
        var coverageOutput = configuration[CoverageOutputSetting];
        ValidateBoundedValue(resultsDirectory, execution.MaximumPathCharacters,
            InvalidResultsDirectoryMessage);
        ValidateBoundedValue(coverageSettings, execution.MaximumPathCharacters,
            InvalidCoverageSettingsPathMessage);
        ValidateBoundedValue(coverageOutput, execution.MaximumPathCharacters,
            InvalidCoverageOutputPathMessage);
        if ((coverageSettings is null) != (coverageOutput is null))
        {
            throw new InvalidOperationException(IncompleteCoverageSettingsMessage);
        }
        var timeout = ReadTimeout(configuration, execution, suite, scaleProfile, vectorProfile);
        return new(suite, project, filter, timeout, resultsDirectory,
            configuration.GetValue<bool>(ReportTrxSetting), coverageSettings, coverageOutput, comparisonTarget,
            localRf3ImageEnabled, scaleProfile) { VectorProfile = vectorProfile, OpenLoopRate = openLoopRate };
    }

    private static TimeSpan ReadTimeout(IConfiguration configuration, TestExecutionOptions execution, string suite,
        ScaledComparisonProfile? scaleProfile, VectorComparisonProfile? vectorProfile)
    {
        var timeout = configuration[TimeoutSetting] is not null
            ? TimeSpan.FromMinutes(configuration.GetValue<int>(TimeoutSetting))
            : scaleProfile is not null || vectorProfile is not null ? execution.IntensiveTimeout
            : suite is TestSuiteProtocol.Rf3Suite or TestSuiteProtocol.ComparisonSuite
                ? execution.ClusterTimeout : execution.OrdinaryTimeout;
        if (!TestExecutionOptions.Bounded(timeout))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        return timeout;
    }

    private static bool HasWorkloadOverride(IConfiguration configuration)
    {
        foreach (var name in WorkloadOverrideNames)
        {
            if (configuration[TestSuiteProtocol.BenchmarkConfigurationSection
                + TestSuiteProtocol.ConfigurationKeySeparator + name] is not null)
            {
                return true;
            }
        }
        return false;
    }

    private static void ValidateBoundedValue(string? value, int maximumLength, string message, bool allowBlank = false)
    {
        const char NullCharacter = '\0';

        if (value is null)
        {
            return;
        }
        if (value.Length > maximumLength || value.Contains(NullCharacter, StringComparison.Ordinal)
            || !allowBlank && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }
    }
}
