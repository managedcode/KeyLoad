using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.StorageRecovery;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Runs strict control parsers inside the single native startup options factory.</summary>
[ConfigurationBinding]
internal static class AppHostControlOptionsRegistration
{
    internal const string InvalidProcessFaultSelection = "Process fault commands require an explicit ephemeral functional or selected KeyLoad load profile.";
    private const string BenchmarkSection = "Benchmarks";
    private const string LoggerModelControlSetting = "KeyLoadTests:LoggerModelControl";
    private const string LoggerModelControlEnabledValue = "true";
    private const string EphemeralSetting = "KeyLoad:Ephemeral";
    private const string BenchmarksEnabledSetting = "Benchmarks:Enabled";
    private const string BenchmarkTargetSetting = "Benchmarks:Target";
    private const string KeyLoadTarget = "KeyLoad";
    private const string BenchmarkSessionSetting = "Benchmarks:Session";
    private const string BenchmarkDataRootSetting = "Benchmarks:DataRoot";
    private const string DataRootSetting = "KeyLoad:DataRoot";
    private const string InvalidLoggerModelControl = "The C1 logger model control selection is invalid.";
    private const string FunctionalNodeCountSetting = "KeyLoadTests:FunctionalNodeCount";
    private const int MinimumFunctionalNodes = 1;
    private const string InvalidFunctionalTopology = "Functional topology requires a positive number of real ephemeral silos and an exclusive owned data root.";
    private const int SectionPresenceCount = 1;

    internal static IOptions<AppHostControlOptions> Bind(IConfiguration configuration)
    {
        var options = new OptionsManager<AppHostControlOptions>(
            new OptionsFactory<AppHostControlOptions>([new ConfigureOptions<AppHostControlOptions>(value =>
            {
                value.Ephemeral = configuration.GetValue<bool>(EphemeralSetting);
                value.BenchmarksEnabled = configuration.GetValue<bool>(BenchmarksEnabledSetting);
                value.TargetSelected = configuration[BenchmarkTargetSetting] is not null;
                value.ComparisonSelectorsPresent = HasSection(configuration, BenchmarkSection);
                value.ProcessFaults = ReadProcessFaults(configuration, value);
                value.LoggerModelControl = ReadLoggerModelControl(configuration, value);
                value.FunctionalColdReopen = FunctionalColdReopenProfile.Read(configuration, value);
                value.FunctionalNodeCount = ReadFunctionalNodeCount(configuration, value);
                value.ExclusiveOwnerContention = ExclusiveOwnerContentionProfile.Read(configuration, value);
            })], [], []));
        _ = options.Value;
        return options;
    }
    private static int? ReadFunctionalNodeCount(IConfiguration configuration, AppHostControlOptions options)
    {
        var section = configuration.GetSection(FunctionalNodeCountSetting);
        var selected = section.Value;
        var nested = section.GetChildren().Take(SectionPresenceCount).Any();
        if (selected is null && !nested)
        {
            return null;
        }
        if (!int.TryParse(selected, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                out var count) || count < MinimumFunctionalNodes || nested
            || configuration[EphemeralSetting] != LoggerModelControlEnabledValue
            || string.IsNullOrWhiteSpace(configuration[DataRootSetting])
            || !Path.IsPathFullyQualified(configuration[DataRootSetting]!)
            || options.BenchmarksEnabled
            || options.TargetSelected || options.ComparisonSelectorsPresent
            || options.ProcessFaults && !options.FunctionalColdReopen
            || options.LoggerModelControl)
        {
            throw new InvalidOperationException(InvalidFunctionalTopology);
        }
        return count;
    }

    private static bool ReadProcessFaults(IConfiguration configuration, AppHostControlOptions options)
    {
        var enabled = configuration.GetValue<bool>(ProcessFaultCommandProtocol.EnabledSetting);
        var selectedLoad = options.BenchmarksEnabled && configuration[BenchmarkTargetSetting] == KeyLoadTarget
            && configuration.GetValue<bool>(BenchmarkSessionSetting)
            && configuration[BenchmarkDataRootSetting] is { } root && Path.IsPathFullyQualified(root);
        if (enabled && (!options.Ephemeral || !selectedLoad && (options.BenchmarksEnabled || options.TargetSelected
            || options.ComparisonSelectorsPresent)))
        {
            throw new InvalidOperationException(InvalidProcessFaultSelection);
        }
        return enabled;
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
            || options.BenchmarksEnabled
            || options.TargetSelected || options.ComparisonSelectorsPresent
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
}
