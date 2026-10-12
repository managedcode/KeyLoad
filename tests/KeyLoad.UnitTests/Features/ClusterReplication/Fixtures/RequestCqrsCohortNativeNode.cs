using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class RequestCqrsCohortNativeNode : IAsyncDisposable
{
    private readonly Lock lifecycle = new();
    private Task? disposal;
    internal ReplicaLifecycleFixture Journal { get; }
    internal ReplicaPeerOptions Peers { get; }
    internal RequestCqrsCohortEndpoint Observation { get; } = new();
    internal ReplicaSiloDiscoveryState Discovery { get; }
    internal ReplicaEnvelopeAuthenticator Authentication { get; }
    internal ReplicaNativeDiscoveryRuntime? Runtime { get; set; }

    internal RequestCqrsCohortNativeNode(string voter, ILocalSiloDetails local,
        ReplicaConfiguration configuration, ReadOnlyMemory<byte> secret,
        RequestCqrsCohortCheckpointScenario? checkpoints = null)
    {
        Journal = new(canonicalObserver: checkpoints?.Observer(voter),
            configuration: configuration with { LocalId = voter });
        try
        {
            checkpoints?.Prepare(voter, Journal);
            Peers = new(new(StringComparer.Ordinal), secret, RequestCqrsCohortScenario.ClusterId)
            { ConnectTimeout = RequestCqrsCohortScenario.ConnectTimeout };
            var configured = UnitExecutionOptions.ReplicaConfiguration(Journal.Configuration);
            var peers = UnitRoutingOptions.Peers(Journal.Configuration, Peers);
            Discovery = new(configured, peers, local, StoreReaderContract.RuntimeJournal);
            Authentication = new(configured, peers, Discovery, TimeProvider.System,
                UnitRoutingOptions.Transport(), UnitRoutingOptions.Replay(Peers.ReplayLimits));
        }
        catch (Exception primary)
        {
            try
            { Journal.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        { return new(disposal ??= DisposeCoreAsync()); }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        if (Runtime is { } runtime)
        {
            await RequestCqrsCohortCleanup.CaptureAsync(runtime.StopAsync, failures);
        }

        await RequestCqrsCohortCleanup.CaptureAsync(() => Journal.DisposeAsync().AsTask(), failures);
        RequestCqrsCohortCleanup.CaptureSync(Authentication.Dispose, failures);
        RequestCqrsCohortCleanup.ThrowIfAny(failures);
    }
}
