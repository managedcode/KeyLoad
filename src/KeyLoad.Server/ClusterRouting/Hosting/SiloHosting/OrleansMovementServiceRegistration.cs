using KeyLoad.Clustering;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server;

internal static class OrleansMovementServiceRegistration
{
    internal static void Register(IServiceCollection services, NodeOptions options)
    {
        global::KeyLoad.Server.Features.ResourceExecution.NativeDropTableRegistration.Register(services);
        services.AddSingleton<IPhysicalRequestPlacement, PhysicalDocumentRequestPlacement>();
        if (options.PhysicalGroupTrust.RemoteDocumentReads)
        {
            services.AddSingleton<IRemoteDocumentReadRouter>(provider => Node(provider).RemoteDocuments?.Router!);
            services.AddSingleton<IControlledDocumentCommandRouter>(provider => Node(provider).RemoteDocuments?.CommandRouter!);
            services.AddSingleton<IRemotePartitionQueryRouter>(provider => Node(provider).RemoteDocuments?.QueryRouter!);
            services.AddSingleton<IRemoteDistributedSearchRouter>(provider => Node(provider).RemoteDocuments?.DistributedSearchRouter!);
            services.AddSingleton<IRemoteAnnReadRouter>(provider => Node(provider).RemoteDocuments?.AnnReadRouter!);
            services.AddSingleton<IEventVectorParent>(provider => Node(provider).RemoteDocuments?.EventVectorParent!);
        }
        services.AddSingleton<global::KeyLoad.Server.Features.ResourceExecution.ResourceOwnerInventoryNativeClient>(provider =>
            Node(provider).ResourceInventoryRuntime!.Client);
        services.AddSingleton<Func<global::KeyLoad.Server.Features.ResourceExecution.ResourceOwnerInventoryNativeRuntime>>(provider =>
        {
            var borrowed = Node(provider).ResourceInventoryRuntime!;
            return () => borrowed;
        });
    }

    private static OrleansNode Node(IServiceProvider services)
    {
        var node = services.GetRequiredService<OrleansNode>();
        node.RequireLocalRuntimePublished();
        return node;
    }
}
