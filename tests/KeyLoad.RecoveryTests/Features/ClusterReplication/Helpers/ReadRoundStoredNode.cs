using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Security;

namespace KeyLoad.RecoveryTests;

internal sealed class ReadRoundStoredNode : IAsyncDisposable
{
    private const string CanonicalDirectory = "canonical";
    private const string ReplicaDirectory = "replica";
    private const string Principal = "root";
    private const string System = "system";
    private const string Wildcard = "*";
    private const int CredentialBytes = 32;
    private readonly ReplicaMaterializerLifecycleStores stores;
    private bool disposed;

    internal ReadRoundStoredNode(string directory, string voter, string[] voters, Guid incarnation, ReadOnlyMemory<byte> signingKey)
    {
        Configuration = new(voter, [.. voters], directory, incarnation) { BenchmarkTopology = voters.Length < 3 };
        stores = ReplicaMaterializerLifecycleStores.Open(
            () => new(new(Path.Combine(directory, CanonicalDirectory)) { Incarnation = incarnation, SigningKey = signingKey }),
            () => new(new(Path.Combine(directory, ReplicaDirectory)) { Incarnation = incarnation }));
        DurableReplicaLog? openedLog = null;
        ReplicaMaterializer? openedMaterializer = null;
        try
        {
            Database = new(stores.Canonical, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
            Log = openedLog = new(stores.Replica, RecoveryExecutionOptions.Configuration(Configuration), canonicalDatabase: Database);
            Database.Bootstrap(new(Principal, System, [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
            { ClusterAdministrator = true }, DatabaseEngine.Credential(Principal, Principal,
                    Convert.ToHexString(RandomNumberGenerator.GetBytes(CredentialBytes))));
            RecoveryPhysicalShardBootstrap.Bootstrap(Database, Principal, Configuration.VoterIds);
            Materializer = openedMaterializer = new(Database, Log, new ReplicaSnapshotStore(stores.Canonical, Log, RecoveryExecutionOptions.Configuration(Configuration), RecoveryExecutionOptions.Replica()), RecoveryExecutionOptions.Replica());
            Consensus = new(Materializer, RecoveryExecutionOptions.Configuration(Configuration), RecoveryExecutionOptions.Replica(), TimeProvider.System);
        }
        catch (Exception error)
        {
            List<Exception> failures = [error];
            if (openedMaterializer is not null)
            {
                ReplicaMaterializerLifecycleErrors.Attempt(() => openedMaterializer.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
            }
            ReplicaMaterializerLifecycleErrors.Attempt(() => openedLog?.Dispose(), failures);
            CloseStores(failures);
            ReplicaMaterializerLifecycleErrors.Throw(failures);
            throw;
        }
    }

    internal ReplicaConfiguration Configuration { get; }
    internal DurableReplicaLog Log { get; }
    internal DatabaseEngine Database { get; }
    internal ReplicaMaterializer Materializer { get; }
    internal ReplicaConsensus Consensus { get; }
    internal ReadRoundProtocolTransport Transport { get; private set; } = null!;

    internal void Attach(IReadOnlyDictionary<string, ReadRoundStoredNode> nodes)
    {
        Transport = new(nodes);
        Consensus.AttachTransport(Transport);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        List<Exception> failures = [];
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => Consensus.DisposeAsync().AsTask(), failures);
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => Materializer.DisposeAsync().AsTask(), failures);
        ReplicaMaterializerLifecycleErrors.Attempt(Log.Dispose, failures);
        CloseStores(failures);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    private void CloseStores(List<Exception> failures)
    {
        try
        { stores.Dispose(); }
        catch (IOException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        catch (UnauthorizedAccessException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        catch (ObjectDisposedException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        catch (InvalidOperationException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        catch (KeyLoadException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        catch (AggregateException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
    }
}
