using KeyLoad.AppHost.Features.BlobStorage;
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
    private const string NativeBlobRootKey = "Root";
    private const string NativeBlobSessionKey = "SessionId";
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
                value.MovementMaxBatchBytes = TwoRf3Profile.ReadMovementMaxBatchBytes(configuration);
                value.MovementMaxFrameBytes = TwoRf3Profile.ReadMovementMaxFrameBytes(configuration);
                value.TenantCapacityPlacement = TenantCapacityPlacementProfile.Validate(
                    configuration[TenantCapacityPlacementProfile.Setting], configuration[TwoRf3ProfileProtocol.Setting],
                    configuration[TwoRf3ProfileProtocol.ProtectedDocumentMovementSetting],
                    configuration[TwoRf3ProfileProtocol.RemoteQuerySetting], configuration[TwoRf3ProfileProtocol.RegistrationSetting]);
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
                value.MovementFrameObservation = MovementFrameObservationSettingsReader.Read(configuration, value);
                value.NativeDiscoveryOmission = NativeDiscoveryOmissionProfile.ReadSettings(configuration, value);
                value.EventVectorReservedProbe = EventVectorReservedProbeProfile.Read(configuration, value);
                value.NativeBlobWireProbe = ReadNativeBlobWireProbe(configuration, value);
            })], [], []));
        _ = options.Value;
        return options;
    }
    private static RequestCqrsProbeProfileSettings? ReadNativeBlobWireProbe(IConfiguration configuration,
        AppHostControlOptions options)
    {
        var section = configuration.GetSection(NativeBlobWireProbeProfile.Section);
        var root = section[NativeBlobRootKey];
        var session = section[NativeBlobSessionKey];
        if (root is null && session is null)
        {
            return null;
        }
        if (!options.Ephemeral || !options.TwoRf3 || options.BenchmarksEnabled || options.ProtocolCohortEnabled
            || !options.RemoteDocumentReads || !options.RemotePartitionQueries || !options.ProtectedDocumentMovement
            || options.RequestProbe is null || session != options.RequestProbe.SessionId
            || string.IsNullOrEmpty(root) || !Path.IsPathFullyQualified(root))
        {
            throw new InvalidOperationException(NativeBlobWireProbeProfile.Invalid);
        }
        return new RequestCqrsProbeProfileSettings(root, session!);
    }

    private static bool ReadLoggerModelControl(IConfiguration configuration, AppHostControlOptions options)
    {
        var control = configuration.GetSection(LoggerModelControlSetting);
        var selected = control.Value;
        var hasNestedSelection = control.GetChildren().Take(SectionPresenceCount).Any();
        if (selected is null && !hasNestedSelection)
        {
            return false;
        }

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
        {
            throw new InvalidOperationException(InvalidLoggerModelControl);
        }

        return true;
    }

    private static bool HasSection(IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);
        return section.Value is not null || section.GetChildren().Take(SectionPresenceCount).Any();
    }

    private static bool HasValue(IConfiguration configuration, string key) => !string.IsNullOrWhiteSpace(configuration[key]);
}
