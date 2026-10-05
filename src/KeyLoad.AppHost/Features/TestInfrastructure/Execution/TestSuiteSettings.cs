using KeyLoad.AppHost.Features.TestInfrastructure.Validation;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

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
    bool LocalRf3ImageEnabled)
{
    internal const string SuiteSetting = "KeyLoadTests:Suite";
    internal const string SuiteEnvironment = "KeyLoadTests__Suite";
    private const string BenchmarkEnabledSetting = "Benchmarks:Enabled";
    private const string FilterSetting = "KeyLoadTests:Filter";
    private const string TimeoutSetting = "KeyLoadTests:TimeoutMinutes";
    private const string ResultsDirectorySetting = "KeyLoadTests:ResultsDirectory";
    private const string ReportTrxSetting = "KeyLoadTests:ReportTrx";
    private const string CoverageSettingsSetting = "KeyLoadTests:CoverageSettings";
    private const string CoverageOutputSetting = "KeyLoadTests:CoverageOutput";
    private const int MaximumFilterLength = 4096;
    private const int MaximumPathLength = 4096;

    internal string ResourceName => "tests-" + Suite;

    internal static bool Requested(string[] args) => !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable(SuiteEnvironment)) || args.Any(argument =>
        argument == "--" + SuiteSetting || argument.StartsWith("--" + SuiteSetting + "=", StringComparison.Ordinal));

    internal static TestSuiteSettings? Read(IConfiguration configuration)
    {
        var suite = configuration[SuiteSetting];
        if (string.IsNullOrWhiteSpace(suite))
        {
            _ = LocalRf3ImageRequest.ReadEnabled(configuration, suite, configuration[FilterSetting]);
            return null;
        }
        var project = suite switch
        {
            "analyzers" => "KeyLoad.Analyzers.Tests",
            "unit" or "unit-scalar" => "KeyLoad.UnitTests",
            "recovery" => "KeyLoad.RecoveryTests",
            "rf3" => "KeyLoad.IntegrationTests",
            "comparison" => "KeyLoad.ComparisonTests",
            "site" => "KeyLoad.SiteTests",
            _ => throw new InvalidOperationException("The Aspire test suite is not supported.")
        };
        var comparisonTarget = configuration[Comparisons.ComparisonWorkerSelection.TargetSetting];
        if (configuration.GetValue<bool>(BenchmarkEnabledSetting)
            || comparisonTarget is not null && suite != "comparison")
        {
            throw new InvalidOperationException("Test and benchmark modes cannot be combined.");
        }
        var filter = configuration[FilterSetting];
        ValidateBoundedValue(filter, MaximumFilterLength, "The test filter is invalid.", allowBlank: true);
        var localRf3ImageEnabled = LocalRf3ImageRequest.ReadEnabled(configuration, suite, filter);
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
        var minutes = configuration.GetValue(TimeoutSetting, suite is "rf3" or "comparison" ? 60 : 30);
        ArgumentOutOfRangeException.ThrowIfLessThan(minutes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minutes, 180);
        return new(suite, project, filter, TimeSpan.FromMinutes(minutes), resultsDirectory,
            configuration.GetValue<bool>(ReportTrxSetting), coverageSettings, coverageOutput, comparisonTarget,
            localRf3ImageEnabled);
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
