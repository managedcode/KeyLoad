using System.Globalization;
using Aspire.Hosting;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class FunctionalColdReopenProfileProtocol
{
    internal const string Nodes = "KeyLoadTests:FunctionalNodeCount";
    internal const string Ephemeral = "KeyLoad:Ephemeral";
    internal const string DataRoot = "KeyLoad:DataRoot";
    private const string SiloCount = "KeyLoad:SiloCount";
    private const string ReplicaCount = "KeyLoad:ReplicaCount";
    internal const string True = "true";
    internal const string False = "false";
    internal const string Nested = ":Nested";
    internal const string LoggerControl = "KeyLoadTests:LoggerModelControl";
    internal const string Benchmark = "Benchmarks:Enabled";
    private const string GuidFormat = "N";
    private const string Prefix = "keyload-functional-cold-profile-";
    internal static string NewRoot() => Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));

    internal static IDistributedApplicationBuilder Create(string root, int nodes, bool cold, bool faults,
        params KeyValuePair<string, string?>[] overrides)
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        var values = new Dictionary<string, string?>
        {
            [Ephemeral] = True,
            [DataRoot] = root,
            [SiloCount] = (nodes == 0 ? 3 : nodes).ToString(CultureInfo.InvariantCulture),
            [ReplicaCount] = (nodes == 0 ? 3 : nodes).ToString(CultureInfo.InvariantCulture),
            [Nodes] = nodes == 0 ? null : nodes.ToString(CultureInfo.InvariantCulture),
            [FunctionalColdReopenProfile.Setting] = cold ? True : null,
            [ProcessFaultCommandProtocol.EnabledSetting] = faults ? True : null
        };
        foreach (var item in overrides)
        {
            values[item.Key] = item.Value;
        }
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }
}
