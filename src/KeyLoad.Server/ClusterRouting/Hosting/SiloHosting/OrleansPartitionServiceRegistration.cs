using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Orleans.Serialization;

namespace KeyLoad.Server;

internal static class OrleansPartitionServiceRegistration
{
    internal static void Register(IServiceCollection services, NodeOptions options)
    {
        services.AddSingleton(provider => Host(provider).Database);
        services.AddSingleton<Func<INativeAnnMaintenance>>(provider =>
        {
            var borrowed = Host(provider).AnnMaintenance;
            return () => borrowed;
        });
        services.AddSingleton<Func<INativeTextMaintenance>>(provider =>
        {
            var borrowed = Host(provider).TextMaintenance;
            return () => borrowed;
        });
        services.AddSingleton<Func<INativeOnlineTextMaintenance>>(provider =>
        {
            var borrowed = Host(provider).OnlineTextMaintenance;
            return () => borrowed;
        });
        services.AddSingleton<Func<ICommitCoordinator>>(provider =>
        {
            var borrowed = Host(provider).Coordinator;
            return () => borrowed;
        });
        services.AddSingleton<SiloDatabaseClient>();
        services.AddSingleton<SiloStorageIdentityProvider>();
        services.AddSingleton(provider =>
        {
            var identity = Host(provider).Database.Store.Identity;
            return new PhysicalShardRecord(identity.NodeId, identity.Incarnation);
        });
        services.AddSingleton<QueryEngine>();
        services.AddSingleton(provider => new SearchEngine(Host(provider).Database,
            provider.GetRequiredService<ServerRuntimeOptions>().Core.QueryExecution, Host(provider).TextProjection,
            Host(provider).AnnMaintenance, provider.GetService<IQueryReadCutObserver>()));
        services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestProgress).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
    }

    private static PartitionHost Host(IServiceProvider services) => services.GetRequiredService<PartitionHostApplicationOwner>().Borrow();
}
