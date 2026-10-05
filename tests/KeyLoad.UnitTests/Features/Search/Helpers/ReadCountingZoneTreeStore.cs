using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class ReadCountingZoneTreeStore : IAtomicStore
{
    private const string DirectoryPrefix = "keyload-graph-search-cut-";
    private const string GuidFormat = "N";
    private const string RootPrincipal = "root";
    private const string Tenant = "tenant";
    private const string PartitionKey = "customer-1";
    private const string RootCredential = "root.graph-search-credential-32-characters";
    private readonly string directory;
    private readonly ZoneTreeStore inner;
    private int readCalls;

    internal ReadCountingZoneTreeStore()
    {
        directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        inner = new(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        Database = new(this, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        Database.Bootstrap(new(RootPrincipal, Tenant, [new("*", "*", Capability.All)], ["*"])
        { ClusterAdministrator = true }, DatabaseEngine.Credential(RootPrincipal, RootPrincipal, RootCredential));
        PhysicalShardTestBootstrap.Bootstrap(Database, RootPrincipal);
        Configure(GraphSearchTestSupport.Documents, ResourceKind.Collection);
        Configure(GraphSearchTestSupport.Projects, ResourceKind.Collection);
        Configure(GraphSearchTestSupport.Graph, ResourceKind.Graph);
        AddGraphData();
        ResetReadCalls();
    }

    internal DatabaseEngine Database { get; }
    internal PartitionRef Partition { get; } = new(Tenant, "database", "orders", PartitionKey);
    internal int ReadCalls => Volatile.Read(ref readCalls);
    public StoreIdentity Identity => inner.Identity;
    public long Position => inner.Position;

    public T Read<T>(Func<IKeyValueView, T> read)
    {
        Interlocked.Increment(ref readCalls);
        return inner.Read(read);
    }

    public T Commit<T>(Func<IAtomicTransaction, long, T> compile) => inner.Commit(compile);
    public void SetDispatchPaused(bool paused) => inner.SetDispatchPaused(paused);
    public long CreateBackup(string path) => inner.CreateBackup(path);
    public StorageSnapshot CreateSnapshot(string path, long? expectedAppliedPosition = null)
        => inner.CreateSnapshot(path, expectedAppliedPosition);
    public StorageSnapshot VerifySnapshot(string path) => inner.VerifySnapshot(path);
    public StorageSnapshot InstallSnapshot(string path, long expectedAppliedPosition)
        => inner.InstallSnapshot(path, expectedAppliedPosition);
    public StorageSnapshot Compact() => inner.Compact();

    internal void ResetReadCalls() => Interlocked.Exchange(ref readCalls, 0);

    public void Dispose()
    {
        try
        {
            inner.Dispose();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private void Configure(string resource, ResourceKind kind)
    {
        var definition = new ResourceDefinition(resource, kind, "orders");
        Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Tenant, "database", definition));
    }

    private void AddGraphData()
    {
        var commandId = Guid.NewGuid();
        Submit(OperationKind.Batch, new CommandRequest(commandId, Partition,
        [
            new PutDocument(GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root, "{}"),
            new PutDocument(GraphSearchTestSupport.Documents, GraphSearchTestSupport.FirstHit, "{}"),
            new PutDocument(GraphSearchTestSupport.Documents, GraphSearchTestSupport.SecondHit, "{}"),
            new UpsertEdge(GraphSearchTestSupport.Graph, "cut-root-first",
                new(Partition, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root),
                new(Partition, GraphSearchTestSupport.Documents, GraphSearchTestSupport.FirstHit),
                GraphSearchTestSupport.Label),
            new UpsertEdge(GraphSearchTestSupport.Graph, "cut-first-second",
                new(Partition, GraphSearchTestSupport.Documents, GraphSearchTestSupport.FirstHit),
                new(Partition, GraphSearchTestSupport.Documents, GraphSearchTestSupport.SecondHit),
                GraphSearchTestSupport.Label)
        ]), id: commandId).Get<CommitReceipt>();
    }

    private OperationResult Submit<T>(OperationKind kind, T payload, Guid? id = null)
        => Database.Apply(new(id ?? Guid.NewGuid(), kind, RootPrincipal, TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(payload, JsonDefaults.Options)));
}
