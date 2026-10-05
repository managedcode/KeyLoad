using KeyLoad;
using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Runs strict control parsers inside the single native startup options factory.</summary>
[ConfigurationBinding]
internal static class AppHostControlOptionsRegistration
{
    internal static IOptions<AppHostControlOptions> Bind(IConfiguration configuration, IOptions<TestExecutionOptions> execution)
    {
        IOptions<AppHostControlOptions> options = new OptionsManager<AppHostControlOptions>(
            new OptionsFactory<AppHostControlOptions>([new ConfigureOptions<AppHostControlOptions>(value =>
            {
                value.Tests = TestSuiteSettings.Read(configuration, execution);
                value.TwoRf3 = TwoRf3Profile.ValidateAndRead(configuration);
                ProtocolCohortImages.ValidateMode(configuration);
                value.RequestProbe = RequestCqrsProbeProfileSettingsReader.Read(configuration);
                value.ProtocolCohortEnabled = configuration.GetValue<bool>(ProtocolCohortImages.EnabledSetting);
                value.Ephemeral = configuration.GetValue<bool>(TwoRf3ProfileProtocol.EphemeralSetting);
                value.BenchmarksEnabled = configuration.GetValue<bool>(TwoRf3ProfileProtocol.BenchmarksEnabledSetting);
                value.TargetSelected = configuration[ComparisonWorkerSelection.TargetSetting] is not null;
                value.ScaleSelected = configuration[ComparisonWorkerSelection.ScaleProfileSetting] is not null;
            })], [], []));
        _ = options.Value;
        return options;
    }
}
