using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

/// <summary>Adds the selected database topology and publishes its endpoints to the benchmark client.</summary>
internal static class BenchmarkTopology
{
    internal static void Add(IDistributedApplicationBuilder builder)
    {
        var topology = BenchmarkTopologyOptions.Read(builder.Configuration);
        var deployment = BenchmarkTopologyOptions.ReadDeployment(builder.Configuration);
        var client = new LoadTestClientSettings();
        builder.Services.AddSingleton(client);
        var selection = new BenchmarkSelection(topology.Database!, topology.Nodes, topology.KeyLoadImage);
        IsolatedDispatcher.Add(new IsolatedResourceContext(builder, selection, client,
            Path.GetFullPath(topology.DataRoot!), deployment));
        LoadTestResourceLifetime.Apply(builder);
    }
}
