using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Orleans.Serialization;

namespace KeyLoad.Server;

internal static class OrleansPartitionServiceRegistration
{
    internal static void Register(IServiceCollection services)
    {
        services.AddScoped<PartitionStorageScope>();
        services.AddScoped(provider => Host(provider).Database);
        services.AddScoped<Func<IAnnMaintenance>>(provider =>
        {
            var borrowed = Host(provider).AnnMaintenance;
            return () => borrowed;
        });
        services.AddScoped<Func<ITextMaintenance>>(provider =>
        {
            var borrowed = Host(provider).TextMaintenance;
            return () => borrowed;
        });
        services.AddScoped<Func<IOnlineTextMaintenance>>(provider =>
        {
            var borrowed = Host(provider).OnlineTextMaintenance;
            return () => borrowed;
        });
        services.AddScoped<Func<ICommitCoordinator>>(provider =>
        {
            var borrowed = Host(provider).Coordinator;
            return () => borrowed;
        });
        services.AddSingleton<SiloDatabaseClient>();
        services.AddSingleton<BackgroundStorageClient>();
        services.AddScoped<SiloStorageIdentityProvider>();
        services.AddScoped(provider =>
        {
            var identity = Host(provider).Database.Store.Identity;
            return new PhysicalShardRecord(identity.NodeId, identity.Incarnation);
        });
        services.AddScoped<QueryEngine>();
        services.AddScoped(provider => new SearchEngine(Host(provider).Database,
            provider.GetRequiredService<ServerRuntimeOptions>().Core.QueryExecution, Host(provider).TextProjection,
            Host(provider).AnnMaintenance, provider.GetService<IQueryReadCutObserver>()));
        services.AddScoped<UserAnalyticsRuntime>();
        services.AddScoped<IUserAnalytics>(provider => provider.GetRequiredService<UserAnalyticsRuntime>());
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<AdminNodeObserver>(provider, Host(provider)));
        services.AddScoped<INodeAdministration>(provider =>
            ActivatorUtilities.CreateInstance<NodeAdministration>(provider, Host(provider)));
        services.AddScoped<IBackgroundStorageOperations, BackgroundStorageOperations>();
        services.AddSerializer(serialization => serialization
            .AddJsonSerializer(type => type == typeof(System.Text.Json.JsonElement))
            .AddAssembly(typeof(GrainRequestProgress).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
    }

    private static PartitionHost Host(IServiceProvider services) => services.GetRequiredService<PartitionStorageScope>().Host;
}
