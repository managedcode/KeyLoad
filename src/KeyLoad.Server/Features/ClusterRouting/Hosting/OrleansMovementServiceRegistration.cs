using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server;

internal static class OrleansMovementServiceRegistration
{
    internal static void Register(IServiceCollection services, NodeOptions options)
    {
        if (options.PhysicalGroupTrust.RemoteDocumentReads)
        {
            services.AddSingleton<IPhysicalRequestPlacement, PhysicalDocumentRequestPlacement>();
            services.AddSingleton<IRemoteDocumentReadRouter>(provider => Node(provider).RemoteDocuments?.Router!);
            services.AddSingleton<IRemoteBlobReadRouter>(provider => Node(provider).RemoteDocuments?.BlobRouter!);
            services.AddSingleton<IRemotePartitionQueryRouter>(provider => Node(provider).RemoteDocuments?.QueryRouter!);
            services.AddSingleton<IRemoteDistributedSearchRouter>(provider => Node(provider).RemoteDocuments?.DistributedSearchRouter!);
            services.AddSingleton<IControlledDocumentCommandRouter>(provider => Node(provider).RemoteDocuments?.CommandRouter!);
            services.AddSingleton<IRemoteAnnReadRouter>(provider => Node(provider).RemoteDocuments?.AnnReadRouter!);
            services.AddSingleton<IEventVectorParent>(provider => Node(provider).RemoteDocuments?.EventVectorParent!);
        }
        services.AddSingleton<Func<INativePartitionMovementCapture>>(provider =>
        {
            var borrowed = Node(provider).Movement?.Source!;
            return () => borrowed;
        });
        services.AddSingleton<Func<IPartitionMovementParent>>(provider =>
        {
            var borrowed = Node(provider).Movement!;
            return () => borrowed;
        });
        services.AddSingleton(provider => Node(provider).Movement?.NativeReceiver!);
        services.AddSingleton(provider => (Node(provider).Movement?.Source as PartitionMovementSourceOwner)?.TransferReads!);
        services.AddSingleton<global::KeyLoad.Server.Features.ResourceExecution.ResourceOwnerInventoryNativeClient>(provider =>
            Node(provider).ResourceInventoryRuntime!.Client);
        services.AddSingleton<Func<global::KeyLoad.Server.Features.ResourceExecution.ResourceOwnerInventoryNativeRuntime>>(provider =>
        {
            var borrowed = Node(provider).ResourceInventoryRuntime!;
            return () => borrowed;
        });
        services.AddSingleton<Func<OwnerCapacityNativeLifetime>>(provider =>
        {
            var borrowed = Node(provider).TenantPlacementRuntime?.Capacity!;
            return () => borrowed;
        });
        if (options.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Control)
        {
            services.AddSingleton<ITenantPlacementPlanner>(provider => Node(provider).TenantPlacementRuntime?.Planner!);
            services.AddSingleton<IPlacementIntentExecutor>(provider => Node(provider).TenantPlacementRuntime?.Executor!);
            services.AddSingleton<IPlacementIntentPersistence>(provider => Node(provider).TenantPlacementRuntime?.Persistence!);
        }
    }

    private static OrleansNode Node(IServiceProvider services)
    {
        var node = services.GetRequiredService<OrleansNode>();
        node.RequireLocalRuntimePublished();
        return node;
    }
}
