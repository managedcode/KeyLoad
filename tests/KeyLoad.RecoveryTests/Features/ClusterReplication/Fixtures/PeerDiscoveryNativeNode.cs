using KeyLoad.Clustering;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;
namespace KeyLoad.RecoveryTests;

internal sealed class PeerDiscoveryNativeNode : IAsyncDisposable
{
    private readonly string directory;
    private readonly ReplicaTermMetadataLifetime journal;
    private readonly ReplicaMaterializer materializer;
    private readonly Lock lifecycle = new();
    private Task? disposal;
    internal ReplicaConsensus Consensus
    {
        get;
    }
    internal ReplicaConfiguration Configuration => journal.Configuration;
    internal ReplicaPeerOptions Peers
    {
        get;
    }
    internal ReplicaSiloDiscoveryState Discovery
    {
        get;
    }
    internal ReplicaEnvelopeAuthenticator Authentication
    {
        get;
    }
    internal ReplicaNativeDiscoveryRuntime? Runtime
    {
        get; set;
    }

    internal PeerDiscoveryNativeNode(ReplicaConfiguration configuration, ILocalSiloDetails local, ReadOnlyMemory<byte> secret)
    {
        directory = ReplicaFixturePaths.NewDirectory("keyload-native-recovery-discovery-");
        ReplicaTermMetadataLifetime? openedJournal = null;
        ReplicaMaterializer? openedMaterializer = null;
        ReplicaConsensus? openedConsensus = null;
        try
        {
            journal = openedJournal = ReplicaTermMetadataLifetime.Open(directory, null,
                configuration with
                {
                    Directory = Path.Combine(directory, "replica")
                });
            var execution = RecoveryExecutionOptions.Replica();
            openedMaterializer = new(journal.Database, journal.BorrowLog(), new ReplicaSnapshotStore(
                journal.Database.Store, journal.BorrowLog(), RecoveryExecutionOptions.Configuration(Configuration), execution), execution);
            materializer = openedMaterializer;
            openedConsensus = new(materializer, RecoveryExecutionOptions.Configuration(Configuration), execution, TimeProvider.System);
            Consensus = openedConsensus;
            Peers = new(new(StringComparer.Ordinal), secret, "recovery-native-discovery")
            {
                ConnectTimeout = TimeSpan.FromMilliseconds(200)
            };
            var configured = RecoveryExecutionOptions.Configuration(Configuration);
            var peers = Options.Create(Peers);
            Discovery = new(configured, peers, local, StoreReaderContract.RuntimeJournal);
            Authentication = new(configured, peers, Discovery, TimeProvider.System,
                Options.Create(new ReplicaTransportOptions()), Options.Create(Peers.ReplayLimits));
        }
        catch (Exception primary)
        {
            List<Exception> failures = [primary];
            if (openedConsensus is not null)
            {
                ReplicaMaterializerLifecycleErrors.Attempt(() => openedConsensus.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
            }
            if (openedMaterializer is not null)
            {
                ReplicaMaterializerLifecycleErrors.Attempt(() => openedMaterializer.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
            }
            if (openedJournal is not null)
            {
                ReplicaMaterializerLifecycleErrors.Attempt(openedJournal.Dispose, failures);
            }
            ReplicaMaterializerLifecycleErrors.Attempt(() => Directory.Delete(directory, true), failures);
            ReplicaMaterializerLifecycleErrors.Throw(failures);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        List<Exception> failures = [];
        if (Runtime is { } runtime)
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(runtime.StopAsync, failures);
        }
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => Consensus.DisposeAsync().AsTask(), failures);
        try
        {
            await materializer.DisposeAsync();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        try
        {
            journal.Dispose();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ReplicaMaterializerLifecycleErrors.Attempt(Authentication.Dispose, failures);
        ReplicaMaterializerLifecycleErrors.Attempt(() => Directory.Delete(directory, true), failures);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }
}
