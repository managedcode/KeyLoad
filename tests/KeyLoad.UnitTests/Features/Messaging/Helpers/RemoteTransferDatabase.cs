using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferDatabase : IDisposable
{
    internal const string RootPrincipal = "root";
    internal const string TenantId = "tenant";
    internal const string DatabaseId = "database";
    internal const string SourceQueueName = "source";
    internal const string DestinationQueueName = "destination";
    internal const string SourcePartitionId = "source-partition";
    internal const string DestinationPartitionId = "destination-partition";
    internal const string OtherSourcePartitionId = "other-source-partition";
    internal const string OtherSourceQueueName = "source-other";
    internal const string Domain = "orders";
    private const string RootCredential = "root.remote-transfer-unit-test-credential";
    private const string DirectoryPrefix = "keyload-remote-transfer-";
    private const string GuidFormat = "N";
    private readonly string directory;
    private readonly DatabaseLimits limits;
    private ZoneTreeStore store;
    private bool disposed;

    internal PartitionRef SourcePartition { get; } = new(TenantId, DatabaseId, Domain, SourcePartitionId);
    internal PartitionRef DestinationPartition { get; } = new(TenantId, DatabaseId, Domain, DestinationPartitionId);
    internal QueueLaneRef SourceQueue => new(SourcePartition, SourceQueueName);
    internal QueueLaneRef DestinationQueue => new(DestinationPartition, DestinationQueueName);
    internal DatabaseEngine Database { get; private set; }
    internal IAtomicStore Store => store;

    internal RemoteTransferDatabase(DatabaseLimits? databaseLimits = null,
        SensitiveFieldPolicy[]? sourceFields = null, SensitiveFieldPolicy[]? sourceHeaders = null,
        SensitiveFieldPolicy[]? destinationFields = null, SensitiveFieldPolicy[]? destinationHeaders = null,
        QueuePolicy? destinationPolicy = null)
    {
        limits = databaseLimits ?? new();
        directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        store = new(new(directory));
        Database = new(store, new AuthorizationPolicy(), limits);
        Database.Bootstrap(new(RootPrincipal, TenantId, [new("*", "*", Capability.All)], ["*"]) { ClusterAdministrator = true },
            DatabaseEngine.Credential(RootPrincipal, RootPrincipal, RootCredential));
        Configure(SourceQueue, sourceFields, sourceHeaders);
        Configure(DestinationQueue, destinationFields, destinationHeaders, destinationPolicy);
    }

    internal CommitReceipt Commit(PartitionRef partition, params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, partition, [.. mutations]);
        return Apply(OperationKind.Batch, request, id: id).Get<CommitReceipt>();
    }

    internal OperationResult Apply<T>(OperationKind kind, T payload, string principal = RootPrincipal,
        Guid? id = null, DateTimeOffset? time = null)
    {
        var commandId = id ?? (payload is CommandRequest command ? command.CommandId : Guid.NewGuid());
        var json = JsonSerializer.Serialize(payload, JsonDefaults.Options);
        return Database.Apply(new(commandId, kind, principal, time ?? TimeProvider.System.GetUtcNow(), json));
    }

    internal void AddPrincipal(PrincipalRecord principal)
        => _ = Apply(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();

    internal RemoteTransferCapacity SourceCapacity(QueueLaneRef lane)
        => Database.Store.Read(view => RemoteTransferStorage.SourceCapacity(view, lane));

    internal RemoteTransferCapacity TargetCapacity(QueueLaneRef lane)
        => Database.Store.Read(view => RemoteTransferStorage.TargetCapacity(view, lane));

    internal void ConfigureQueue(QueueLaneRef lane, SensitiveFieldPolicy[]? fields = null,
        SensitiveFieldPolicy[]? headers = null, QueuePolicy? queuePolicy = null)
        => Configure(lane, fields, headers, queuePolicy);

    internal static long MeasureTargetReceiptGrowth(string firstId, string secondId, string payload)
    {
        using var measured = new RemoteTransferDatabase();
        var otherSource = new QueueLaneRef(new(TenantId, DatabaseId, Domain, OtherSourcePartitionId),
            OtherSourceQueueName);
        measured.ConfigureQueue(otherSource);
        var first = new CreateQueueTransfer(measured.SourceQueue, Guid.NewGuid(), measured.DestinationQueue,
            new(measured.DestinationQueue.Queue, firstId, payload));
        var second = new CreateQueueTransfer(otherSource, Guid.NewGuid(), measured.DestinationQueue,
            new(measured.DestinationQueue.Queue, secondId, payload));
        measured.Commit(measured.SourcePartition, first);
        measured.Commit(otherSource.Partition, second);
        var firstIntent = measured.Database.InspectQueueTransfer(RootPrincipal,
            measured.SourceQueue, first.TransferId)!;
        var secondIntent = measured.Database.InspectQueueTransfer(RootPrincipal,
            otherSource, second.TransferId)!;
        measured.Commit(measured.DestinationPartition,
            new AcceptQueueTransfer(measured.DestinationQueue, firstIntent.IntentToken));
        var before = measured.TargetCapacity(measured.DestinationQueue).StoredBytes;
        measured.Commit(measured.DestinationPartition,
            new AcceptQueueTransfer(measured.DestinationQueue, secondIntent.IntentToken));
        return measured.TargetCapacity(measured.DestinationQueue).StoredBytes - before;
    }

    internal void Reopen(DatabaseLimits? replacementLimits = null)
    {
        store.Dispose();
        store = new(new(directory));
        Database = new(store, new AuthorizationPolicy(), replacementLimits ?? limits);
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
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private void Configure(QueueLaneRef lane, SensitiveFieldPolicy[]? fields, SensitiveFieldPolicy[]? headers,
        QueuePolicy? queuePolicy = null)
    {
        var resource = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, lane.Partition.TransactionDomainId)
        {
            FieldPolicies = fields is null ? [] : [.. fields],
            HeaderPolicies = headers is null ? [] : [.. headers],
            QueuePolicy = queuePolicy ?? new()
        };
        _ = Apply(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(TenantId, DatabaseId, resource)).Get<ResourceDefinition>();
    }
}
