using KeyLoad;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Reads the exact bounded trusted AppHost control settings and excludes incompatible modes.</summary>
[ConfigurationBinding]
internal static class RequestCqrsProbeProfileSettingsReader
{
    private const string NodesArrayText = "node1";
    private const string NodesNodesArrayText = "node2";
    private const int ValidSessionStructuralValue = 32;

    private const string EphemeralSetting = "KeyLoad:Ephemeral";
    private const string BenchmarkSetting = "Benchmarks:Enabled";
    private const string BenchmarkProfileSetting = "Benchmarks:Profile";
    private const string TimeSeriesBenchmarkSection = "Benchmarks:TimeSeries";
    private const string InvalidConfiguration = "RequestCqrsProbeConfigurationInvalid";
    private const string TrueValue = "true";
    private const string FalseValue = "false";
    private const string GuidFormat = "N";
    private const int MaximumSettings = 4;
    private const string EnabledKey = "Enabled";
    private const string RootKey = "Root";
    private const string SessionKey = "SessionId";
    private const string DiscoveryModeKey = "DiscoveryCaptureMode";
    private const string DisabledMode = "disabled";
    private const string MixedMode = "mixed-interface3-v1";
    private static readonly IReadOnlyList<string> Nodes = Array.AsReadOnly(new[] { NodesArrayText, NodesNodesArrayText, "node3" });

    internal static IReadOnlyList<string> VoterNames => Nodes;

    internal static RequestCqrsProbeProfileSettings? Read(IConfiguration configuration)
    {
        const int Step = 1;
        const int CountValue = 1;

        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(RequestCqrsProbeProfile.Section);
        var fields = section.GetChildren().Take(MaximumSettings + Step).ToArray();
        if (section.Value is not null || fields.Length > MaximumSettings)
        { throw new InvalidOperationException(InvalidConfiguration); }
        var enabledFound = false;
        var rootFound = false;
        var sessionFound = false;
        string? enabledText = null;
        string? root = null;
        string? session = null;
        var modeFound = false;
        string? mode = null;
        foreach (var field in fields)
        {
            if (field.GetChildren().Take(CountValue).Any() || field.Value is null)
            { throw new InvalidOperationException(InvalidConfiguration); }
            switch (field.Key)
            {
                case EnabledKey:
                    enabledFound = true;
                    enabledText = field.Value;
                    break;
                case RootKey:
                    rootFound = true;
                    root = field.Value;
                    break;
                case SessionKey:
                    sessionFound = true;
                    session = field.Value;
                    break;
                case DiscoveryModeKey:
                    modeFound = true;
                    mode = field.Value;
                    break;
                default:
                    throw new InvalidOperationException(InvalidConfiguration);
            }
        }
        var enabled = ReadEnabled(enabledFound, enabledText);
        if (!enabled)
        {
            ValidateDisabledSettings(rootFound, sessionFound, session, modeFound, mode);
            return null;
        }
        ValidateEnabledSettings(configuration, rootFound, root, sessionFound, session, modeFound, mode);
        return new RequestCqrsProbeProfileSettings(root!, session!, mode ?? DisabledMode);
    }

    private static void ValidateDisabledSettings(bool rootFound, bool sessionFound, string? session,
        bool modeFound, string? mode)
    {
        const int StructuralValue = 0;

        if (rootFound || (sessionFound && session is not { Length: StructuralValue })
            || modeFound && mode != DisabledMode)
        { throw new InvalidOperationException(InvalidConfiguration); }
    }

    private static bool ReadEnabled(bool found, string? value)
    {
        if (!found)
        { return false; }
        if (value is TrueValue)
        { return true; }
        if (value is FalseValue)
        { return false; }
        throw new InvalidOperationException(InvalidConfiguration);
    }

    private static void ValidateEnabledSettings(IConfiguration configuration, bool rootFound, string? root,
        bool sessionFound, string? session, bool modeFound, string? mode)
    {
        var mixedMode = mode == MixedMode;
        if (!rootFound || string.IsNullOrWhiteSpace(root) || !sessionFound || !ValidSession(session)
            || modeFound && mode is not (DisabledMode or MixedMode)
            || configuration[EphemeralSetting] != TrueValue
            || !string.IsNullOrWhiteSpace(configuration[TestSuiteSettings.SuiteSetting])
            || HasBenchmarkSelection(configuration)
            || HasProtocolCohortOverride(configuration) != mixedMode)
        { throw new InvalidOperationException(InvalidConfiguration); }
    }

    private static bool HasBenchmarkSelection(IConfiguration configuration)
    {
        const int CountValue = 1;

        var timeSeries = configuration.GetSection(TimeSeriesBenchmarkSection);
        return ReadBenchmark(configuration)
            || configuration[BenchmarkProfileSetting] is not null
            || configuration[Comparisons.ComparisonWorkerSelection.TargetSetting] is not null
            || configuration[Comparisons.ComparisonWorkerSelection.NodeCountSetting] is not null
            || timeSeries.Value is not null || timeSeries.GetChildren().Take(CountValue).Any();
    }

    private static bool ReadBenchmark(IConfiguration configuration)
    {
        var value = configuration[BenchmarkSetting];
        if (value is null or FalseValue)
        { return false; }
        if (value is TrueValue)
        { return true; }
        throw new InvalidOperationException(InvalidConfiguration);
    }

    private static bool HasProtocolCohortOverride(IConfiguration configuration)
    {
        const int CountValue = 1;

        ProtocolCohortImages.ValidateMode(configuration);
        return configuration.GetSection(ProtocolCohortImages.EnabledSetting).Value is TrueValue
            || configuration.GetSection(ProtocolCohortImages.VotersSetting).Value is not null
            || configuration.GetSection(ProtocolCohortImages.VotersSetting).GetChildren().Take(CountValue).Any();
    }

    private static bool ValidSession(string? value)
        => value is { Length: ValidSessionStructuralValue } && Guid.TryParseExact(value, GuidFormat, out var session)
            && session != Guid.Empty && session.ToString(GuidFormat) == value;
}
