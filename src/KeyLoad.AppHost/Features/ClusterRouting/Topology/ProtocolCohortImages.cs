using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Admits only explicit ephemeral RF3 protocol-test image overrides.</summary>
internal static class ProtocolCohortImages
{
    internal const string EnabledSetting = "KeyLoadTests:ProtocolCohort:Enabled";
    internal const string VotersSetting = "KeyLoadTests:ProtocolCohort:Voters";
    private const string SectionSetting = "KeyLoadTests:ProtocolCohort";
    private const string EphemeralSetting = "KeyLoad:Ephemeral";
    private const string BenchmarkSetting = "Benchmarks:Enabled";
    private const string InvalidConfiguration = "ProtocolCohortTestConfigurationInvalid";
    private const string EnabledKey = "Enabled";
    private const string VotersKey = "Voters";
    private static readonly string[] Voters = ["node1", "node2", "node3"];

    internal static void ValidateMode(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = ReadChildren(configuration.GetSection(SectionSetting), 2);
        if (section.Any(child => child.Key is not (EnabledKey or VotersKey))
            || configuration.GetSection(EnabledSetting).GetChildren().Any())
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }

        var selected = ReadChildren(configuration.GetSection(VotersSetting), Voters.Length);
        if (!configuration.GetValue<bool>(EnabledSetting))
        {
            if (selected.Length != 0)
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
        ArgumentNullException.ThrowIfNull(builder);
        ValidateMode(builder.Configuration);
        var enabled = builder.Configuration.GetValue<bool>(EnabledSetting);
        if (enabled && (!ephemeral || benchmarkNodeCount is not null))
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }

        var defaultImage = enabled ? null
            : RuntimeContainerImage.Read(builder, RuntimeContainerImage.ServerConfiguration);
        return Voters.ToDictionary(voter => voter,
            voter => defaultImage ?? RuntimeContainerImage.Read(builder, VotersSetting + ":" + voter),
            StringComparer.Ordinal);
    }

    private static IConfigurationSection[] ReadChildren(IConfigurationSection section, int maximum)
    {
        var children = section.GetChildren().Take(maximum + 1).ToArray();
        if (section.Value is not null || children.Length > maximum)
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }
        return children;
    }
}
