using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RecurringSagaDatabase : IDisposable
{
    internal const string RootPrincipal = "root";
    internal const string TenantId = "tenant";
    internal const string DatabaseId = "database";
    internal const string QueueName = "jobs";
    internal const string TimeoutQueueName = "timeouts";
    internal const string SecretPath = "/secret";
    internal const string RawReadGrant = "pii.read";
    internal const string RawUseGrant = "pii.use";
    internal const string FieldWriteGrant = "pii.write";
    internal static readonly DateTimeOffset Epoch = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal const string Domain = "orders";
    private const string PartitionId = "orders-1";
    private const string Credential = "root.recurring-saga-unit-test-credential";
    private const string DirectoryPrefix = "keyload-recurring-saga-";
    private const string GuidFormat = "N";
    private readonly string directory;
    private readonly DatabaseLimits limits;
    private ZoneTreeStore store;
    private bool disposed;

    internal PartitionRef Partition { get; } = new(TenantId, DatabaseId, Domain, PartitionId);
    internal QueueLaneRef Queue => new(Partition, QueueName);
    internal QueueLaneRef TimeoutQueue => new(Partition, TimeoutQueueName);
    internal DatabaseEngine Database { get; private set; }
    internal IAtomicStore Store => store;

    internal RecurringSagaDatabase(DatabaseLimits? databaseLimits = null, SensitiveFieldPolicy[]? fields = null,
        SensitiveFieldPolicy[]? headers = null, QueuePolicy? queuePolicy = null,
        QueuePolicy? timeoutQueuePolicy = null)
    {
        limits = databaseLimits ?? new();
        directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        store = new(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        Database = new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        Database.Bootstrap(new(RootPrincipal, TenantId, [new("*", "*", Capability.All)], ["*"])
        { ClusterAdministrator = true }, DatabaseEngine.Credential(RootPrincipal, RootPrincipal, Credential));
        PhysicalShardTestBootstrap.Bootstrap(Database, RootPrincipal);
        ConfigureQueueDefinition(Queue, fields, headers, queuePolicy, null);
        ConfigureQueueDefinition(TimeoutQueue, null, null, timeoutQueuePolicy, null);
    }

    internal OperationResult Apply<T>(OperationKind kind, T payload, string principal = RootPrincipal,
        Guid? id = null, DateTimeOffset? time = null)
    {
        var commandId = id ?? (payload is CommandRequest command ? command.CommandId : Guid.NewGuid());
        var json = JsonSerializer.Serialize(payload, JsonDefaults.Options);
        return Database.Apply(new(commandId, kind, principal, time ?? Epoch, json));
    }

    internal CommitReceipt Commit(PartitionRef partition, params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Apply(OperationKind.Batch, new CommandRequest(id, partition, [.. mutations]), id: id).Get<CommitReceipt>();
    }

    internal CommitReceipt CommitAs(string principal, PartitionRef partition, DateTimeOffset time,
        params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Apply(OperationKind.Batch, new CommandRequest(id, partition, [.. mutations]), principal, id, time)
            .Get<CommitReceipt>();
    }

    internal void AddPrincipal(PrincipalRecord principal, DateTimeOffset? time = null)
    {
        var previous = store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(principal.Id)));
        var updated = previous is null ? principal : principal with { PolicyEpoch = checked(previous.PolicyEpoch + 1) };
        _ = Apply(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(updated), time: time)
            .Get<PrincipalRecord>();
    }

    internal void ConfigureQueue(QueueLaneRef lane, QueuePolicy? queuePolicy = null, DateTimeOffset? time = null)
        => ConfigureQueueDefinition(lane, null, null, queuePolicy, time);

    internal PrincipalRecord Principal(string id)
        => store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(id)))!;

    internal void Reopen()
    {
        store.Dispose();
        store = new(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        Database = new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        PhysicalShardTestBootstrap.RequireExisting(Database);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            store.Dispose();
        }
        finally
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, true);
            }
        }
    }

    private void ConfigureQueueDefinition(QueueLaneRef lane, SensitiveFieldPolicy[]? fields,
        SensitiveFieldPolicy[]? headers, QueuePolicy? queuePolicy, DateTimeOffset? time)
    {
        var resource = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, lane.Partition.TransactionDomainId)
        {
            FieldPolicies = fields is null ? [] : [.. fields],
            HeaderPolicies = headers is null ? [] : [.. headers],
            QueuePolicy = queuePolicy ?? new()
        };
        _ = Apply(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(TenantId, DatabaseId, resource), time: time).Get<ResourceDefinition>();
    }
}
