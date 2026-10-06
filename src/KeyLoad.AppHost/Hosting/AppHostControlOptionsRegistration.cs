using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;
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
            })], [], []));
        _ = options.Value;
        return options;
    }
    private static bool HasValue(IConfiguration configuration, string key) => !string.IsNullOrWhiteSpace(configuration[key]);
}
