using KeyLoad.Server.Features.ClusterRouting;
using Orleans.Configuration;

namespace KeyLoad.Server;

/// <summary>Composes native heterogeneous membership without creating database owners or advertising database grains.</summary>
internal static class CoordinationSiloHosting
{
    internal static async Task<WebApplication> BuildAsync(WebApplicationBuilder builder,
        CoordinationSiloOptions options, CancellationToken token)
    {
        var address = await OrleansNodeAddressResolver.ResolveAsync(options.SiloAddress,
            new Uri(options.PublicEndpoint).IsLoopback, token).ConfigureAwait(false);
        builder.Services.AddSingleton(options);
        OrleansNativeClustering.RegisterHostingOptions(builder);
        builder.UseOrleans(silo =>
        {
            silo.Configure<ClusterOptions>(cluster =>
            {
                cluster.ClusterId = options.ClusterId;
                cluster.ServiceId = OrleansNodeProtocol.ServiceId;
            });
            OrleansNativeClustering.ConfigureMembership(silo, options.PublicEndpoint, options.ClusterId, options.SiloPort, address, null);
            // Keep Orleans infrastructure grains; database implementations require a physical owner.
            silo.Services.PostConfigure<GrainTypeOptions>(types =>
                types.Classes.RemoveWhere(type => type.Assembly != typeof(Silo).Assembly));
            silo.AddActivityPropagation();
#pragma warning disable ORLEANSEXP003
            silo.AddDistributedGrainDirectory();
#pragma warning restore ORLEANSEXP003
#pragma warning disable ORLEANSEXP001
            silo.AddActivationRepartitioner();
#pragma warning restore ORLEANSEXP001
        });
        var app = builder.Build();
        app.MapDefaultEndpoints();
        app.MapGet(ServerProtocol.ReadyPath, (ISiloStatusOracle status) =>
            status.CurrentStatus == SiloStatus.Active
                && status.GetApproximateSiloStatuses(onlyActive: true).Count == options.SiloCount
                ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        return app;
    }
}
