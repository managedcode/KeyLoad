using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.StorageRecovery;

/// <summary>Selects only explicit cold CLUSTER functional competing-owner refusal resources.</summary>
[ConfigurationBinding]
internal static class ExclusiveOwnerContentionProfile
{
    internal const string Setting = "KeyLoadTests:ExclusiveOwnerContention";
    internal const string EnabledArgument = "--KeyLoadTests:ExclusiveOwnerContention=true";
    internal const string Invalid = "Exclusive owner contention requires an explicit ephemeral cold CLUSTER functional topology.";
    private const string Enabled = "true";
    private const int ThreeVoters = 3;
    private const int SectionPresence = 1;

    internal static bool Read(IConfiguration configuration, AppHostControlOptions options)
    {
        var section = configuration.GetSection(Setting);
        var selected = section.Value;
        var nested = section.GetChildren().Take(SectionPresence).Any();
        if (selected is null && !nested)
        {
            return false;
        }
        if (selected != Enabled || nested || !options.Ephemeral || options.FunctionalNodeCount != ThreeVoters
            || !options.FunctionalColdReopen || !options.ContainerFaults || options.Tests is not null
            || options.BenchmarksEnabled || options.TargetSelected
            || options.ComparisonSelectorsPresent
            || options.LoggerModelControl)
        {
            throw new InvalidOperationException(Invalid);
        }
        return true;
    }
}
