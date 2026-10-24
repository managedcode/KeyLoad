using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.StorageRecovery;

/// <summary>Selects only explicit cold CLUSTER functional competing-owner refusal resources.</summary>
[ConfigurationBinding]
internal static class ExclusiveOwnerContentionProfile
{
    internal const string Setting = "KeyLoadTests:ExclusiveOwnerContention";
    internal const string EnabledArgument = "--KeyLoadTests:ExclusiveOwnerContention=true";
    internal const string Invalid = "Exclusive owner contention requires an explicit ephemeral cold CLUSTER functional topology.";
    private const string Enabled = "true";
    private const int RequiredFunctionalSilos = 3;

    internal static bool Read(IConfiguration configuration, AppHostControlOptions options)
    {
        var section = configuration.GetSection(Setting);
        var selected = section.Value;
        var nested = section.GetChildren().Take(1).Any();
        if (selected is null && !nested)
        {
            return false;
        }
        if (selected != Enabled || nested || !options.Ephemeral || options.FunctionalNodeCount != RequiredFunctionalSilos
            || !options.FunctionalColdReopen || !options.ProcessFaults
            || options.BenchmarksEnabled || options.TargetSelected
            || options.ComparisonSelectorsPresent
            || options.LoggerModelControl)
        {
            throw new InvalidOperationException(Invalid);
        }
        return true;
    }
}
