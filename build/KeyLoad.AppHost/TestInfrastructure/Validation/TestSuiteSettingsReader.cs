using KeyLoad.AppHost.Features.CodeQuality;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.TestInfrastructure.Validation;

[ConfigurationBinding]
internal static class TestSuiteSettingsReader
{
    private const string ConflictingExecutionModesMessage = "Test and benchmark modes cannot be combined.";
    private const string InvalidTestFilterMessage = "The test filter is invalid.";
    private const string InvalidResultsDirectoryMessage = "The results directory is invalid.";
    private const string InvalidCoverageSettingsPathMessage = "The coverage settings path is invalid.";
    private const string InvalidCoverageOutputPathMessage = "The coverage output path is invalid.";
    private const string IncompleteCoverageSettingsMessage = "Coverage settings and output must be configured together.";
    private const string ResultsDirectorySetting = "KeyLoadTests:ResultsDirectory";
    private const string ReportTrxSetting = "KeyLoadTests:ReportTrx";
    private const string CoverageSettingsSetting = "KeyLoadTests:CoverageSettings";
    private const string CoverageOutputSetting = "KeyLoadTests:CoverageOutput";
    internal static TestSuiteSettings? Read(IConfiguration configuration, IOptions<TestExecutionOptions> execution,
        IOptions<NativeCoverageExecutionOptions> coverage)
    {
        var suite = configuration[TestSuiteSettings.SuiteSetting];
        var filter = configuration[TestSuiteSettings.FilterSetting];
        TestSuiteSelectionValidator.RejectRetiredBenchmarkSelection(configuration);
        if (string.IsNullOrWhiteSpace(suite))
        {
            return ReadWithoutSuite(configuration, suite, filter);
        }
        return ReadSelectedSuite(configuration, execution.Value, coverage, suite, filter);
    }

    private static TestSuiteSettings? ReadWithoutSuite(IConfiguration configuration, string? suite, string? filter)
    {
        NativeCoverageSelection.RejectWithoutSuite(configuration);
        _ = LocalClusterImageRequest.ReadEnabled(configuration, suite, filter);
        return null;
    }

    private static TestSuiteSettings ReadSelectedSuite(IConfiguration configuration, TestExecutionOptions execution,
        IOptions<NativeCoverageExecutionOptions> coverage,
        string suite, string? filter)
    {
        var project = TestSuiteSelectionValidator.ProjectForSuite(suite);
        if (configuration.GetSection(TestSuiteProtocol.BenchmarkConfigurationSection).Exists())
        {
            throw new InvalidOperationException(ConflictingExecutionModesMessage);
        }
        ValidateBoundedValue(filter, execution.MaximumFilterCharacters,
            InvalidTestFilterMessage, allowBlank: true);
        var localClusterImageEnabled = LocalClusterImageRequest.ReadEnabled(configuration, suite, filter);
        return CreateSettings(configuration, execution, coverage, suite, project, filter, localClusterImageEnabled);
    }

    private static TestSuiteSettings CreateSettings(IConfiguration configuration, TestExecutionOptions execution,
        IOptions<NativeCoverageExecutionOptions> coverage,
        string suite, string project, string? filter, bool localClusterImageEnabled)
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
        var coverageFormat = NativeCoverageSelection.Read(configuration, suite, coverageSettings, coverageOutput);
        var nativeCoverageCluster = NativeCoverageClusterSelection.Read(configuration, suite, filter,
            coverageSettings, coverageOutput, coverageFormat, coverage);
        if (nativeCoverageCluster is not null && localClusterImageEnabled)
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
        }
        var timeout = ReadTimeout(configuration, execution, suite);
        var parallelTests = TestSuiteParallelism.Read(configuration, execution, suite);
        return new(suite, project, filter, timeout, parallelTests, resultsDirectory,
            configuration.GetValue<bool>(ReportTrxSetting), coverageSettings, coverageOutput, localClusterImageEnabled)
        {
            CoverageFormat = coverageFormat,
            NativeCoverageCluster = nativeCoverageCluster
        };
    }

    private static TimeSpan ReadTimeout(IConfiguration configuration, TestExecutionOptions execution, string suite)
    {
        var timeout = configuration[TestSuiteSettings.TimeoutSetting] is not null
            ? TimeSpan.FromMinutes(configuration.GetValue<int>(TestSuiteSettings.TimeoutSetting))
            : suite is TestSuiteProtocol.ClusterSuite or TestSuiteProtocol.ComparisonSuite
                ? execution.ClusterTimeout : execution.OrdinaryTimeout;
        return ValidateTimeout(timeout);
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout)
    {
        if (!TestExecutionOptions.Bounded(timeout))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        return timeout;
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
