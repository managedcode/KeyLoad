using System.Globalization;
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
    internal VectorComparisonProfile? VectorProfile { get; init; }
    internal const string VectorProfileSetting = "KeyLoadTests:VectorProfile";
    internal const string VectorProfileEnvironment = "KeyLoadTests__VectorProfile";
    internal const string SuiteSetting = "KeyLoadTests:Suite";
    internal const string SuiteEnvironment = "KeyLoadTests__Suite";
    internal const string ScaleProfileSetting = "KeyLoadTests:ScaleProfile";
    internal const string ScaleProfileEnvironment = "KeyLoadTests__ScaleProfile";
    private const string BenchmarkEnabledSetting = "Benchmarks:Enabled";
    private const string FilterSetting = "KeyLoadTests:Filter";
    private const string TimeoutSetting = "KeyLoadTests:TimeoutMinutes";
    private const string ResultsDirectorySetting = "KeyLoadTests:ResultsDirectory";
    private const string ReportTrxSetting = "KeyLoadTests:ReportTrx";
    private const string CoverageSettingsSetting = "KeyLoadTests:CoverageSettings";
    private const string CoverageOutputSetting = "KeyLoadTests:CoverageOutput";
    private const int MaximumFilterLength = 4096;
    private static readonly string[] WorkloadOverrideNames =
        ["Documents", "Operations", "Warmup", "Repetitions", "Concurrency", "PayloadBytes", "Seed", "Dimensions",
            "TopK", "TimeoutSeconds", "GraphVertices", "GraphFanOut", "GraphDepth"];
    private const int MaximumPathLength = 4096;

    internal string ResourceName => "tests-" + Suite;

    internal static bool Requested(string[] args)
    {
        var vectorArgument = "--" + VectorProfileSetting + "=";
        if (args.Any(argument => argument == "--" + VectorProfileSetting || argument == vectorArgument))
        {
            throw new InvalidOperationException("The vector-profile test selection is invalid.");
        }
        var scaleArgument = "--" + ScaleProfileSetting + "=";
        if (args.Any(argument => argument == "--" + ScaleProfileSetting || argument == scaleArgument))
        {
            throw new InvalidOperationException("The scale-profile test selection is invalid.");
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SuiteEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VectorProfileEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ScaleProfileEnvironment))
            || args.Any(argument => argument == "--" + SuiteSetting
                || argument.StartsWith(TestSuiteProtocol.ArgumentPrefix + SuiteSetting + TestSuiteProtocol.ArgumentValueSeparator, StringComparison.Ordinal)
                || argument == "--" + ScaleProfileSetting
                || argument.StartsWith(scaleArgument, StringComparison.Ordinal)
                || argument == "--" + VectorProfileSetting
                || argument.StartsWith(vectorArgument, StringComparison.Ordinal));
    }

    internal static TestSuiteSettings? Read(IConfiguration configuration)
        => Read(configuration, AppHostOptionsRegistration.BindTestExecution(configuration));

    internal static TestSuiteSettings? Read(IConfiguration configuration, IOptions<TestExecutionOptions> execution)
    {
        var suite = configuration[SuiteSetting];
        if (string.IsNullOrWhiteSpace(suite))
        {
            _ = LocalRf3ImageRequest.ReadEnabled(configuration, suite, configuration[FilterSetting]);
            if (!string.IsNullOrEmpty(configuration[ScaleProfileSetting]))
            {
                throw new InvalidOperationException("The scale-profile test selection is invalid.");
            }
            if (!string.IsNullOrEmpty(configuration[VectorProfileSetting]))
            {
                throw new InvalidOperationException("The vector-profile test selection is invalid.");
            }
            return null;
        }
        var project = suite switch
        {
            TestSuiteProtocol.AnalyzersSuite => "KeyLoad.Analyzers.Tests",
            TestSuiteProtocol.UnitSuite or TestSuiteProtocol.ScalarUnitSuite => "KeyLoad.UnitTests",
            TestSuiteProtocol.RecoverySuite => "KeyLoad.RecoveryTests",
            TestSuiteProtocol.Rf3Suite => "KeyLoad.IntegrationTests",
            TestSuiteProtocol.ComparisonSuite => "KeyLoad.ComparisonTests",
            TestSuiteProtocol.SiteSuite => "KeyLoad.SiteTests",
            _ => throw new InvalidOperationException("The Aspire test suite is not supported.")
        };
        var comparisonTarget = configuration[ComparisonWorkerSelection.TargetSetting];
        var scaleProfile = ReadScaleProfile(configuration, suite, comparisonTarget);
        var vectorProfile = VectorTestSuiteSelection.Read(configuration, suite, comparisonTarget, HasWorkloadOverride(configuration));
        if (scaleProfile is not null && vectorProfile is not null)
        {
            throw new InvalidOperationException("Scale and vector test profiles cannot be combined.");
        }
        if (configuration.GetValue<bool>(BenchmarkEnabledSetting)
            || comparisonTarget is not null && suite != TestSuiteProtocol.ComparisonSuite)
        {
            throw new InvalidOperationException("Test and benchmark modes cannot be combined.");
        }
        var filter = configuration[FilterSetting];
        ValidateBoundedValue(filter, MaximumFilterLength, "The test filter is invalid.", allowBlank: true);
        var localRf3ImageEnabled = LocalRf3ImageRequest.ReadEnabled(configuration, suite, filter);
        if (localRf3ImageEnabled && (scaleProfile is not null || vectorProfile is not null))
        {
            throw new InvalidOperationException("Test and benchmark modes cannot be combined.");
        }
        var resultsDirectory = configuration[ResultsDirectorySetting];
        var coverageSettings = configuration[CoverageSettingsSetting];
        var coverageOutput = configuration[CoverageOutputSetting];
        ValidateBoundedValue(resultsDirectory, MaximumPathLength, "The results directory is invalid.");
        ValidateBoundedValue(coverageSettings, MaximumPathLength, "The coverage settings path is invalid.");
        ValidateBoundedValue(coverageOutput, MaximumPathLength, "The coverage output path is invalid.");
        if ((coverageSettings is null) != (coverageOutput is null))
        {
            throw new InvalidOperationException("Coverage settings and output must be configured together.");
        }
        var policy = execution.Value;
        var timeout = configuration[TimeoutSetting] is not null
            ? TimeSpan.FromMinutes(configuration.GetValue<int>(TimeoutSetting))
            : scaleProfile is not null || vectorProfile is not null ? policy.IntensiveTimeout
            : suite is TestSuiteProtocol.Rf3Suite or TestSuiteProtocol.ComparisonSuite ? policy.ClusterTimeout : policy.OrdinaryTimeout;
        if (!TestExecutionOptions.Bounded(timeout))
        { throw new ArgumentOutOfRangeException(nameof(timeout)); }
        return new(suite, project, filter, timeout, resultsDirectory,
            configuration.GetValue<bool>(ReportTrxSetting), coverageSettings, coverageOutput, comparisonTarget,
            localRf3ImageEnabled, scaleProfile) { VectorProfile = vectorProfile };
    }

    private static ScaledComparisonProfile? ReadScaleProfile(IConfiguration configuration, string suite, string? target)
    {
        var value = configuration[ScaleProfileSetting];
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        if (suite != TestSuiteProtocol.ComparisonSuite || target is null
            || !IsolatedComparisonContract.Current.Targets.Contains(target, StringComparer.Ordinal)
            || configuration.GetValue<bool>(BenchmarkEnabledSetting)
            || configuration[TimeoutSetting] is { } timeout && timeout != TestSuiteProtocol.ProfileTimeoutMinutesText
            || configuration[ComparisonWorkerSelection.ProfileSetting] is not { } evidenceProfile
            || configuration[ComparisonWorkerSelection.ScaleProfileSetting] is not null
            || configuration[FilterSetting] != TestSuiteProtocol.IsolatedComparisonFilter
            || !HasValidScaleWorkload(configuration)
            || HasWorkloadOverride(configuration))
        {
            throw new InvalidOperationException("The scale-profile test selection is invalid.");
        }
        try
        {
            var profile = ScaledComparisonProfileParser.Parse(value);
            if (!string.Equals(profile.Id, evidenceProfile, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The scale-profile test selection is invalid.");
            }
            return profile;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException("The scale-profile test selection is invalid.");
        }
    }

    private static bool HasValidScaleWorkload(IConfiguration configuration)
    {
        if (!int.TryParse(configuration[ComparisonWorkerSelection.NodeCountSetting], NumberStyles.None,
                CultureInfo.InvariantCulture, out var nodeCount)
            || !IsolatedComparisonContract.Current.NodeCounts.Contains(nodeCount))
        {
            return false;
        }

        var text = configuration[ComparisonWorkerSelection.ScenarioSetting];
        if (!Enum.TryParse<Scenario>(text, out var scenario) || !Enum.IsDefined(scenario)
            || !string.Equals(text, scenario.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        return scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete;
    }

    private static bool HasWorkloadOverride(IConfiguration configuration)
    {
        foreach (var name in WorkloadOverrideNames)
        {
            if (configuration["Benchmarks:" + name] is not null)
            {
                return true;
            }
        }
        return false;
    }

    private static void ValidateBoundedValue(string? value, int maximumLength, string message, bool allowBlank = false)
    {
        if (value is null)
        {
            return;
        }
        if (value.Length > maximumLength || value.Contains('\0', StringComparison.Ordinal) || !allowBlank && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }
    }
}
