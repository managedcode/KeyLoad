using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Owns complete Aspire resource composition for the KeyLoad AppHost.</summary>
internal static class KeyLoadAppHostApplication
{
    /// <summary>Composes the simultaneous Docker RF3 cluster and optional comparison dependencies.</summary>
    internal static void AddKeyLoad(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var configuration = global::AppHostConfiguration.Read(builder);
        var profile = global::ClusterProfileStore.Open(configuration.DataRoot);
        var nodes = global::ClusterResources.Add(builder, profile, configuration.DataRoot, configuration.Ephemeral);
        if (configuration.BenchmarkMode)
        {
            var admin = builder.CreateResourceBuilder(builder.Resources.OfType<ParameterResource>()
                .Single(resource => resource.Name == global::AppHostConfiguration.AdminParameter));
            if (string.Equals(configuration.BenchmarkProfile, global::AppHostConfiguration.TimeSeriesBenchmarkProfile,
                    StringComparison.OrdinalIgnoreCase))
            {
                TimeSeriesBenchmarkResources.Add(builder, nodes, admin, configuration.BenchmarkRoot);
            }
            else
            {
                global::BenchmarkResources.Add(builder, nodes, admin, configuration.BenchmarkRoot);
            }
        }
    }
}
