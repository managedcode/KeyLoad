using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.TestInfrastructure.Validation;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Runs strict control parsers inside the single native startup options factory.</summary>
[ConfigurationBinding]
internal static class AppHostControlOptionsRegistration
{
    private const string ProtocolSection = "KeyLoadTests:ProtocolCohort";
    private const string BenchmarkEnabled = "Benchmarks:Enabled";
    private const string BenchmarkScale = "Benchmarks:ScaleProfile";
    private const string BenchmarkScenario = "Benchmarks:Scenario";
    private const string BenchmarkProfile = "Benchmarks:Profile";
    private const string BenchmarkNodes = "Benchmarks:NodeCount";
    private const string BenchmarkTimeSeries = "Benchmarks:TimeSeries";
    private const string LoggerModelControlSetting = "KeyLoadTests:LoggerModelControl";
    private const string LoggerModelControlEnabledValue = "true";
    private const string EphemeralSetting = "KeyLoad:Ephemeral";
    private const string DataRootSetting = "KeyLoad:DataRoot";
    private const string InvalidLoggerModelControl = "The C1 logger model control selection is invalid.";
    private const int SectionPresenceCount = 1;

    internal static IOptions<AppHostControlOptions> Bind(IConfiguration configuration, IOptions<TestExecutionOptions> execution,
        IOptions<NativeCoverageExecutionOptions> coverage)
    {
        var options = new OptionsManager<AppHostControlOptions>(
            new OptionsFactory<AppHostControlOptions>([new ConfigureOptions<AppHostControlOptions>(value =>
            {
                value.RequestProbe = RequestCqrsProbeProfileSettingsReader.Read(configuration);
                value.Tests = TestSuiteSettings.Read(configuration, execution, coverage);
                value.TwoRf3 = TwoRf3Profile.ValidateAndRead(configuration);
                value.RemoteDocumentReads = configuration[TwoRf3ProfileProtocol.RemoteDocumentSetting] == TwoRf3ProfileProtocol.Enabled;
                value.RemotePartitionQueries = configuration[TwoRf3ProfileProtocol.RemoteQuerySetting] == TwoRf3ProfileProtocol.Enabled;
                value.ProtectedDocumentMovement = configuration[TwoRf3ProfileProtocol.ProtectedDocumentMovementSetting] == TwoRf3ProfileProtocol.Enabled;
                ProtocolCohortImages.ValidateMode(configuration);
                value.ProtocolCohortEnabled = configuration.GetValue<bool>(ProtocolCohortImages.EnabledSetting);
                value.Ephemeral = configuration.GetValue<bool>(TwoRf3ProfileProtocol.EphemeralSetting);
                value.BenchmarksEnabled = configuration.GetValue<bool>(TwoRf3ProfileProtocol.BenchmarksEnabledSetting);
                value.TargetSelected = configuration[ComparisonWorkerSelection.TargetSetting] is not null;
                value.ProtocolCohortConfigured = configuration.GetSection(ProtocolSection).GetChildren().Take(SectionPresenceCount).Any();
                value.ComparisonSelectorsPresent = HasValue(configuration, BenchmarkEnabled)
                    || HasValue(configuration, ComparisonWorkerSelection.TargetSetting) || HasValue(configuration, BenchmarkNodes)
                    || HasValue(configuration, BenchmarkScenario) || HasValue(configuration, BenchmarkProfile) || HasValue(configuration, BenchmarkScale);
                value.ScaleSelected = configuration[ComparisonWorkerSelection.ScaleProfileSetting] is not null;
                value.LoggerModelControl = ReadLoggerModelControl(configuration, value);
            })], [], []));
        _ = options.Value;
        return options;
    }
    private static bool ReadLoggerModelControl(IConfiguration configuration, AppHostControlOptions options)
    {
        var control = configuration.GetSection(LoggerModelControlSetting);
        var selected = control.Value;
        var hasNestedSelection = control.GetChildren().Take(SectionPresenceCount).Any();
        if (selected is null && !hasNestedSelection)
        { return false; }

        if (selected != LoggerModelControlEnabledValue
            || hasNestedSelection
            || options.Tests is not null || options.RequestProbe is not null || options.TwoRf3
            || options.ProtocolCohortEnabled || options.ProtocolCohortConfigured || options.BenchmarksEnabled
            || options.TargetSelected || options.ComparisonSelectorsPresent || options.ScaleSelected
            || configuration[ComparisonWorkerSelection.OpenLoopRateSetting] is not null
            || configuration[ComparisonWorkerSelection.VectorProfileSetting] is not null
            || configuration[TestSuiteSelectionValidator.OpenLoopRateSetting] is not null
            || configuration[TestSuiteSelectionValidator.OpenLoopCancellationProofSetting] is not null
            || HasSection(configuration, BenchmarkTimeSeries)
            || configuration[LocalRf3ImageRequest.EnabledSetting] == LoggerModelControlEnabledValue
            || configuration[EphemeralSetting] != LoggerModelControlEnabledValue
            || string.IsNullOrWhiteSpace(configuration[DataRootSetting]))
        { throw new InvalidOperationException(InvalidLoggerModelControl); }

        return true;
    }

    private static bool HasSection(IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);
        return section.Value is not null || section.GetChildren().Take(SectionPresenceCount).Any();
    }

    private static bool HasValue(IConfiguration configuration, string key) => !string.IsNullOrWhiteSpace(configuration[key]);
}
