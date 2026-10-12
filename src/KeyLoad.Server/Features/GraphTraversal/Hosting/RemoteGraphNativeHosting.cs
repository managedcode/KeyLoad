using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphNativeHosting
{
    internal static void Register(IServiceCollection services, PartitionHost partition, ServerRuntimeOptions options,
        TimeProvider clock, RemoteGraphRuntime? runtime)
    {
        services.AddSingleton(provider => new RemoteGraphNativeRuntime(partition, options.Node, options.Membership,
            options.GrainRouting, provider.GetRequiredService<global::Orleans.Runtime.ISiloStatusOracle>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(), clock,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RemoteGraphNativeRuntime>>()));
        services.AddSingleton(provider => new RemoteGraphNativeClient(provider, partition, options.Node, options.Membership,
            options.GrainRouting, provider.GetRequiredService<PartitionMovementGrainServiceClient>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(), clock));
        if (runtime is not null)
        { services.AddSingleton(runtime.Parent); }
    }
}
