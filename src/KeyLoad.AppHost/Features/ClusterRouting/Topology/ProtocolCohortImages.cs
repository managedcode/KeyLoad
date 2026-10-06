using KeyLoad;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Admits only explicit ephemeral RF3 protocol-test image overrides.</summary>
[ConfigurationBinding]
internal static class ProtocolCohortImages
{
    private const string ThirdVoterName = "node3";
    private const int ProtocolSectionFieldCount = 2;

    private const string VotersResultText = "node1";
    private const string VotersVotersResultText = "node2";

    internal const string EnabledSetting = "KeyLoadTests:ProtocolCohort:Enabled";
    internal const string VotersSetting = "KeyLoadTests:ProtocolCohort:Voters";
    private const string SectionSetting = "KeyLoadTests:ProtocolCohort";
    private const string EphemeralSetting = "KeyLoad:Ephemeral";
    private const string BenchmarkSetting = "Benchmarks:Enabled";
    private const string InvalidConfiguration = "ProtocolCohortTestConfigurationInvalid";
    private const string EnabledKey = "Enabled";
    private const string VotersKey = "Voters";
    private static readonly string[] Voters = [VotersResultText, VotersVotersResultText, ThirdVoterName];

    internal static void ValidateMode(IConfiguration configuration)
    {
        const int EmptyValue = 0;

        ArgumentNullException.ThrowIfNull(configuration);
        var section = ReadChildren(configuration.GetSection(SectionSetting), ProtocolSectionFieldCount);
        if (section.Any(child => child.Key is not (EnabledKey or VotersKey))
            || configuration.GetSection(EnabledSetting).GetChildren().Any())
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }

        var selected = ReadChildren(configuration.GetSection(VotersSetting), Voters.Length);
        if (!configuration.GetValue<bool>(EnabledSetting))
        {
            if (selected.Length != EmptyValue)
            {
                throw new InvalidOperationException(InvalidConfiguration);
            }
            return;
        }

        if (!configuration.GetValue<bool>(EphemeralSetting)
            || !string.IsNullOrWhiteSpace(configuration[TestSuiteSettings.SuiteSetting])
            || configuration.GetValue<bool>(BenchmarkSetting)
            || configuration[Comparisons.ComparisonWorkerSelection.TargetSetting] is not null
            || selected.Length != Voters.Length
            || selected.Any(child => !Voters.Contains(child.Key, StringComparer.Ordinal)
                || child.Value is null || child.GetChildren().Any()))
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }
    }

    internal static IReadOnlyDictionary<string, RuntimeContainerImage> Read(
        IDistributedApplicationBuilder builder, bool ephemeral, int? benchmarkNodeCount)
    {
        const string ConfigurationKeyText = ":";

        ArgumentNullException.ThrowIfNull(builder);
        var enabled = KeyLoad.AppHost.Hosting.AppHostOptionsRegistration.Get(builder).Control.Value.ProtocolCohortEnabled;
        if (enabled && (!ephemeral || benchmarkNodeCount is not null))
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }

        var defaultImage = enabled ? null
            : RuntimeContainerImage.Read(builder, RuntimeContainerImage.ServerConfiguration);
        return Voters.ToDictionary(voter => voter,
            voter => defaultImage ?? RuntimeContainerImage.Read(builder, VotersSetting + ConfigurationKeyText + voter),
            StringComparer.Ordinal);
    }

    private static IConfigurationSection[] ReadChildren(IConfigurationSection section, int maximum)
    {
        const int Step = 1;

        var children = section.GetChildren().Take(maximum + Step).ToArray();
        if (section.Value is not null || children.Length > maximum)
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }
        return children;
    }
}
