using System.Net;
using KeyLoad.Core;
using KeyLoad.Orleans;
using ManagedCode.Orleans.Graph.Extensions;
using Orleans.Configuration;

namespace KeyLoad.Server;

public sealed class OrleansNode(DatabaseEngine database, ICommitCoordinator coordinator, NodeOptions options)
{
    private IHost? host;
    public IGrainFactory? Grains { get; private set; }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(database).AddSingleton(coordinator);
        builder.Services.AddSingleton<IMembershipTable>(new ConsensusMembershipTable(database, coordinator, options.ClusterId, "root"));
        builder.UseOrleans(silo =>
        {
            silo.Configure<ClusterOptions>(cluster => { cluster.ClusterId = options.ClusterId; cluster.ServiceId = "KeyLoad"; });
            silo.ConfigureEndpoints(IPAddress.Parse(options.SiloAddress), options.SiloPort, 0);
            silo.Configure<ClusterMembershipOptions>(membership =>
            { membership.IAmAliveTablePublishTimeout = TimeSpan.FromSeconds(5); membership.TableRefreshTimeout = TimeSpan.FromSeconds(5); });
            silo.AddOrleansGraph(configureGraph: graph => graph.AllowClientCallGrain<ICommandRouterGrain>());
        });
        host = builder.Build();
        await host.StartAsync(cancellationToken);
        Grains = host.Services.GetRequiredService<IGrainFactory>();
    }
    public async Task<OperationResult> SubmitAsync(OperationKind kind, Guid id, string principal, string json, CancellationToken cancellationToken)
    {
        if (Grains is null) throw Errors.Fail(ErrorCode.OwnershipLost, "The Orleans routing layer is not ready.");
        var envelope = database.Sign(new GrainCommandEnvelope("grain-command", kind, id, principal, json, DateTimeOffset.UtcNow.AddMinutes(1)));
        var outcome = await Grains.GetGrain<ICommandRouterGrain>("shard:0").ExecuteAsync(envelope).WaitAsync(cancellationToken);
        return JsonDefaults.Deserialize<OperationResult>(System.Text.Encoding.UTF8.GetBytes(outcome));
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Grains = null;
        if (host is not null) { await host.StopAsync(cancellationToken); host.Dispose(); }
    }
}
