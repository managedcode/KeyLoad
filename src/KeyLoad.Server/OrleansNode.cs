using System.Net;
using KeyLoad.Core;
using KeyLoad.Orleans;
using ManagedCode.Orleans.Graph.Extensions;
using Orleans.Configuration;

namespace KeyLoad.Server;

public sealed class OrleansNode(DatabaseEngine database, ICommitCoordinator coordinator, NodeOptions options, ILogger<OrleansNode> logger)
{
    private IHost? host;
    public IGrainFactory? Grains { get; private set; }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await coordinator.ReadBarrierAsync(cancellationToken);
                await StartAttemptAsync(cancellationToken);
                return;
            }
            catch (KeyLoadException exception) when (exception.Code is ErrorCode.OwnershipLost or ErrorCode.UnknownWriteOutcome or ErrorCode.ResourceExhausted)
            {
                logger.LogWarning("Orleans startup is waiting for consensus: {ErrorCode}", exception.Code);
                var failed = host; host = null; Grains = null;
                if (failed is not null)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await failed.StopAsync(cleanup.Token); }
                    catch (OperationCanceledException) { }
                    catch (KeyLoadException cleanupFailure) when (cleanupFailure.Code is ErrorCode.OwnershipLost or ErrorCode.UnknownWriteOutcome or ErrorCode.ResourceExhausted)
                    { logger.LogWarning("Failed Orleans startup cleanup is waiting for consensus: {ErrorCode}", cleanupFailure.Code); }
                    finally { failed.Dispose(); }
                }
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }
    private async Task StartAttemptAsync(CancellationToken cancellationToken)
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
        var stopping = host; host = null;
        if (stopping is not null)
        {
            try { await stopping.StopAsync(cancellationToken); }
            finally { stopping.Dispose(); }
        }
    }
}
