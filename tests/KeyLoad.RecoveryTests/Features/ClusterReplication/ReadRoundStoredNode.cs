using System.Security.Cryptography;
using KeyLoad.Core;
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

    internal ReadRoundStoredNode(string directory, string voter, string[] voters, Guid incarnation)
    {
        Configuration = new(voter, [.. voters], directory, incarnation) { BenchmarkTopology = voters.Length < 3 };
        stores = ReplicaMaterializerLifecycleStores.Open(
            () => new(new(Path.Combine(directory, CanonicalDirectory)) { Incarnation = incarnation }),
            () => new(new(Path.Combine(directory, ReplicaDirectory)) { Incarnation = incarnation }));
        DurableReplicaLog? openedLog = null;
        ReplicaMaterializer? openedMaterializer = null;
        try
        {
            Log = openedLog = new(stores.Replica, Configuration);
            Database = new(stores.Canonical, new AuthorizationPolicy());
            Database.Bootstrap(new(Principal, System, [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
            { ClusterAdministrator = true }, DatabaseEngine.Credential(Principal, Principal,
                    Convert.ToHexString(RandomNumberGenerator.GetBytes(CredentialBytes))));
            Materializer = openedMaterializer = new(Database, Log, new ReplicaSnapshotStore(stores.Canonical, Log, Configuration));
            Consensus = new(Materializer, Configuration, TimeProvider.System);
        }
        catch (Exception error)
        {
            List<Exception> failures = [error];
            if (openedMaterializer is not null)
            {
                try
                {
                    ReplicaMaterializerLifecycleErrors.Attempt(() => openedMaterializer.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
                }
                catch (AggregateException cleanup) { ReplicaMaterializerLifecycleErrors.Add(cleanup, failures); }
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
        await CloseAsync(() => Consensus.DisposeAsync().AsTask(), failures);
        await CloseAsync(() => Materializer.DisposeAsync().AsTask(), failures);
        ReplicaMaterializerLifecycleErrors.Attempt(Log.Dispose, failures);
        try
        {
            stores.Dispose();
        }
        catch (Exception error) when (error is AggregateException or IOException or UnauthorizedAccessException
            or InvalidOperationException or KeyLoadException)
        { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    private static async Task CloseAsync(Func<Task> action, List<Exception> failures)
    {
        try
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(action, failures);
        }
        catch (AggregateException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
    }

    private void CloseStores(List<Exception> failures)
    {
        try
        {
            ReplicaMaterializerLifecycleErrors.Attempt(stores.Dispose, failures);
        }
        catch (AggregateException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
    }
}
