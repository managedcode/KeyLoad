using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateReplayFixture : IDisposable
{
    internal const string RootId = "root";
    internal const string WorkerId = "aggregate-worker";
    internal const string StreamSet = "replay-events";
    internal const string StreamId = "aggregate-1";
    internal const string QueueName = "replay-queue";
    internal const string QueueMessageId = "replay-queue-message";
    internal const string PayloadSecretPath = "/secret";
    internal const string HeaderSecretPath = "/privateHeader";
    internal const string PayloadReadGrant = "aggregate.payload.read";
    internal const string PayloadUseGrant = "aggregate.payload.use";
    internal const string HeaderReadGrant = "aggregate.header.read";
    internal const string HeaderUseGrant = "aggregate.header.use";
    internal const string Reducer = "orders.reducer.v1";
    internal const string StateJson = "{ \"count\" : 2, \"nested\" : [true, null] }";
    private const string SystemTenant = "system";
    private const string Wildcard = "*";
    private const string RootCredential = "root.aggregate-test-credential-32-characters";
    private const string DirectoryPrefix = "keyload-aggregate-replay-";
    private const string GuidFormat = "N";
    private const string FreshDirectoryMessage = "The fixture must own a fresh directory.";
    private const string PayloadPrivate = "aggregate-private";
    private const string HeaderPrivate = "aggregate-header-private";
    internal const string EventType = "AccountChanged";
    private const string TenantId = "tenant";
    private const string DatabaseId = "database";
    private const string DomainId = "orders";
    private const string PartitionKey = "customer-1";
    private const string StreamHeadSpace = "stream-head";
    private const string EventSpace = "event";
    private const string EventIdentitySpace = "event-id";
    private readonly string directory;
    private ZoneTreeStore store { get; set; } = null!;
    private DatabaseEngine database { get; set; } = null!;

    internal PartitionRef Partition { get; } = new(TenantId, DatabaseId, DomainId, PartitionKey);
    internal DatabaseEngine Database => database;
    internal ZoneTreeStore Store => store;

    internal AggregateReplayFixture(DatabaseLimits? limits = null, bool protectedFields = false,
        TimeProvider? timeProvider = null)
    {
        directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        if (Directory.Exists(directory))
        {
            throw new InvalidOperationException(FreshDirectoryMessage);
        }
        try
        {
            Open(limits, timeProvider);
            database.Bootstrap(new(RootId, SystemTenant, [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
            { ClusterAdministrator = true }, DatabaseEngine.Credential(RootId, RootId, RootCredential));
            PhysicalShardTestBootstrap.Bootstrap(database, RootId);
            ConfigureResource(protectedFields);
            ConfigurePrincipal(WorkerId, Capability.EventsReplay | Capability.EventsRead | Capability.EventsSnapshotsManage,
                protectedFields ? [PayloadReadGrant, PayloadUseGrant, HeaderReadGrant, HeaderUseGrant] : []);
        }
        catch (Exception)
        {
            try
            {
                store?.Dispose();
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            throw;
        }
    }

    internal void ConfigurePrincipal(string id, Capability capabilities, string[] fieldGrants, long policyEpoch = 1,
        bool revoked = false)
    {
        var principal = new PrincipalRecord(id, Partition.TenantId,
            [new(Partition.DatabaseId, StreamSet, capabilities)], [.. fieldGrants])
        { PolicyEpoch = policyEpoch, Revoked = revoked };
        Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal));
    }

    internal void Append(params EventData[] events)
        => Commit(new AppendEvents(StreamSet, StreamId, [.. events], ExpectedStreamRevision.Any));

    internal void EnqueueQueueMessage()
        => Commit(new EnqueueMessage(QueueName, QueueMessageId, "{}"));

    internal CommitReceipt Commit(params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, Partition, [.. mutations]);
        var operation = new ReplicatedOperation(id, OperationKind.Batch, RootId, TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(request, JsonDefaults.Options));
        return database.Apply(operation).Get<CommitReceipt>();
    }

    internal MessageInspection? InspectQueueMessage()
        => database.InspectMessage(RootId, new QueueLaneRef(Partition, QueueName), QueueMessageId);

    internal CommitReceipt StoreSnapshot(long sourceRevision, long expectedVersion = 0,
        long generation = 1, string? stateJson = null, string? reducer = null, int schemaVersion = 1,
        Guid? commandId = null, string principal = WorkerId)
    {
        var command = new StoreAggregateSnapshot(StreamSet, StreamId, sourceRevision, reducer ?? Reducer,
            schemaVersion, stateJson ?? StateJson, expectedVersion, generation);
        var id = commandId ?? Guid.NewGuid();
        var operation = new ReplicatedOperation(id, OperationKind.Batch, principal, TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(new CommandRequest(id, Partition, [command]), JsonDefaults.Options));
        return database.Apply(operation).Get<CommitReceipt>();
    }

    internal AggregateReplayPage Read(string? principal = null, int maximumEvents = 100,
        string? reducer = null, int schemaVersion = 1, bool fromBeginning = false, long generation = 1,
        CancellationToken cancellationToken = default)
        => database.ReadAggregateReplay(principal ?? WorkerId,
            new(new(Partition, StreamSet, StreamId, generation), reducer ?? Reducer, schemaVersion, fromBeginning,
                maximumEvents), cancellationToken);

    internal AggregateSnapshotState Snapshot()
    {
        var bytes = store.Read(view => view.ReadOwnedValue(SnapshotKey()));
        return bytes is null ? throw new InvalidOperationException("The snapshot is absent.")
            : AggregateSnapshotPersistence.Deserialize(bytes, database.Limits);
    }

    internal byte[] SnapshotKey(long generation = 1)
        => KeySpace.Partition(AggregateSnapshotPersistence.SnapshotSpace, Partition, StreamSet, StreamId, generation);

    internal byte[] EventKey(long revision, long generation = 1)
        => KeySpace.Partition(EventSpace, Partition, StreamSet, StreamId, generation, revision);

    internal byte[] EventIdentityKey(string eventId, long generation = 1)
        => KeySpace.Partition(EventIdentitySpace, Partition, StreamSet, StreamId, generation, eventId);

    internal void RetainFrom(long firstAvailableRevision)
        => Store.Commit((transaction, _) =>
        {
            var headKey = KeySpace.Partition(StreamHeadSpace, Partition, StreamSet, StreamId);
            var head = transaction.GetRecord<StreamHead>(headKey) ?? throw new InvalidOperationException();
            transaction.PutRecord(headKey, head with { FirstAvailableRevision = firstAvailableRevision });
            return true;
        });

    internal void StoreEnvelope(AggregateSnapshotEnvelope envelope)
        => Store.Commit((transaction, _) =>
        {
            transaction.Put(SnapshotKey(), NativeSerialization.Serialize(envelope));
            return true;
        });

    internal void Reopen(DatabaseLimits? limits = null, TimeProvider? timeProvider = null)
    {
        store.Dispose();
        Open(limits, timeProvider);
        PhysicalShardTestBootstrap.RequireExisting(database);
    }

    internal long Position => Store.Position;

    public void Dispose()
    {
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

    private void ConfigureResource(bool protectedFields)
    {
        var resource = new ResourceDefinition(StreamSet, ResourceKind.StreamSet, Partition.TransactionDomainId)
        {
            FieldPolicies = protectedFields ? [new(PayloadSecretPath, PayloadPrivate, PayloadReadGrant, PayloadUseGrant,
                RequiredForProcessing: false)] : [],
            HeaderPolicies = protectedFields ? [new(HeaderSecretPath, HeaderPrivate, HeaderReadGrant, HeaderUseGrant,
                RequiredForProcessing: false)] : []
        };
        Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource));
        var queue = new ResourceDefinition(QueueName, ResourceKind.WorkQueue, Partition.TransactionDomainId);
        Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, queue));
    }

    private void Open(DatabaseLimits? limits, TimeProvider? timeProvider)
    {
        store = new(new(directory));
        database = new(store, new AuthorizationPolicy(), limits, timeProvider);
    }

    private void Submit<T>(OperationKind kind, T payload)
        => database.Apply(new(Guid.NewGuid(), kind, RootId, TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(payload, JsonDefaults.Options))).Get<object>();
}
