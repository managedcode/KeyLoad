using System.Text;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed class ReplicaIsolationNativeRetirementFixture : IDisposable
{
    private const string Prefix = "keyload-native-retirement-";
    private const string Format = "N";
    private const string NodeLock = "node.owner.lock";
    private const string Database = "database";
    private const string Replica = "replica";
    private const string StoreLock = "owner.lock";
    private const string KeyText = "retirement/committed";
    private const string ValueText = "literal committed native row";
    private const int StoresPerNode = 2;
    private const int LocksPerNode = 3;
    private const long CommittedPosition = 1;
    private readonly string root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(Format));
    private readonly TimeProvider clock;
    private readonly List<ZoneTreeStore> stores = [];
    private readonly List<FileStream> nodes = [];
    private readonly List<string> paths = [];
    private readonly byte[] key = Encoding.UTF8.GetBytes(KeyText);
    private readonly byte[] value = Encoding.UTF8.GetBytes(ValueText);
    private bool rootCreated;
    private bool disposed;
    private bool ownerCloseFailed;

    internal ReplicaIsolationNativeRetirementFixture(TimeProvider clock) => this.clock = clock;

    internal void Initialize()
    {
        Directory.CreateDirectory(root);
        rootCreated = true;
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    internal async Task VerifyHeldOwnersAsync()
    {
        for (var index = ReplicaIsolationFlowProtocol.First; index <= ReplicaIsolationFlowProtocol.Voters; index++)
        {
            var node = Path.Combine(root, ClusterFixtureProtocol.NodeName(index));
            Directory.CreateDirectory(node);
            nodes.Add(new FileStream(Path.Combine(node, NodeLock), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
            foreach (var directory in new[] { Database, Replica })
            {
                var path = Path.Combine(node, directory);
                var store = Open(path);
                stores.Add(store);
                paths.Add(path);
                store.Commit((transaction, _) => { transaction.Put(key, value); return true; });
            }
        }
        await Assert.That(stores.Count).IsEqualTo(ReplicaIsolationFlowProtocol.Voters * StoresPerNode);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => ReplicaIsolationRetirement.AssertLocks(root, []), failures);
        await Assert.That(failures.Count).IsEqualTo(ReplicaIsolationFlowProtocol.First);
        await Assert.That(failures.Single() is AggregateException).IsTrue();
        var denied = (AggregateException)failures.Single();
        await Assert.That(denied.InnerExceptions.Count).IsEqualTo(ReplicaIsolationFlowProtocol.Voters * LocksPerNode);
        await Assert.That(denied.InnerExceptions.All(error => error is IOException and not FileNotFoundException)).IsTrue();
    }

    internal void CloseOwners()
    {
        var failures = new List<Exception>();
        var ownedStores = stores.ToArray();
        var ownedNodes = nodes.ToArray();
        stores.Clear();
        nodes.Clear();
        foreach (var store in ownedStores)
        { ServerFailureObserver.Observe(store.Dispose, failures); }
        foreach (var node in ownedNodes)
        { ServerFailureObserver.Observe(node.Dispose, failures); }
        ownerCloseFailed |= failures.Count > ReplicaIsolationFlowProtocol.Zero;
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task VerifyRetiredAndReopenedAsync()
    {
        var evidence = new List<ReplicaIsolationRetirementObservation>();
        ReplicaIsolationRetirement.AssertLocks(root, evidence);
        await Assert.That(evidence.Count).IsEqualTo(ReplicaIsolationFlowProtocol.Voters * LocksPerNode);
        await Assert.That(evidence.All(item => item.ExclusiveLockAcquired)).IsTrue();
        await Assert.That(evidence.Count(item => Path.GetFileName(item.Target) == StoreLock))
            .IsEqualTo(ReplicaIsolationFlowProtocol.Voters * StoresPerNode);
        foreach (var path in paths)
        {
            ZoneTreeStore? reopened = null;
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reopened = Open(path);
                var row = reopened.Read(view => view.ReadOwnedValue(key));
                await Assert.That(row is not null && row.AsSpan().SequenceEqual(value)).IsTrue();
                await Assert.That(reopened.Position).IsEqualTo(CommittedPosition);
            }, failures);
            if (reopened is { } owned)
            { ServerFailureObserver.Observe(owned.Dispose, failures); }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private ZoneTreeStore Open(string path) => new(new(path), IntegrationExecutionOptions.StorageExecution(),
        IntegrationExecutionOptions.PointCacheExecution(), clock);

    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(CloseOwners, failures);
        if (!ownerCloseFailed && paths.Count == ReplicaIsolationFlowProtocol.Voters * StoresPerNode
            && failures.Count == ReplicaIsolationFlowProtocol.Zero)
        { ServerFailureObserver.Observe(() => ReplicaIsolationRetirement.AssertLocks(root, []), failures); }
        if (rootCreated && !ownerCloseFailed && failures.Count == ReplicaIsolationFlowProtocol.Zero)
        { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
