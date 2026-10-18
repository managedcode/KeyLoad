using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Admits native faults only for an explicit cold functional test topology.</summary>
[ConfigurationBinding]
internal static class FunctionalColdReopenProfile
{
    internal const string Setting = "KeyLoadTests:FunctionalColdReopen";
    internal const string EnabledArgument = "--KeyLoadTests:FunctionalColdReopen=true";
    internal const string Invalid = "The functional cold-reopen selection requires explicit one or three silo native process faults.";
    private const string FunctionalNodes = "KeyLoadTests:FunctionalNodeCount";
    private const string Enabled = "true";
    private const string Single = "1";
    private const string Three = "3";

    internal static bool Read(IConfiguration configuration, AppHostControlOptions options)
    {
        var section = configuration.GetSection(Setting);
        var value = section.Value;
        var nested = section.GetChildren().Any();
        if (value is null && !nested)
        {
            return false;
        }
        if (value != Enabled || nested || !options.ProcessFaults
            || configuration[ProcessFaultCommandProtocol.EnabledSetting] != Enabled
            || configuration[FunctionalNodes] is not (Single or Three))
        {
            throw new InvalidOperationException(Invalid);
        }
        return true;
    }
}
