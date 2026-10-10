using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class StreamReadResourceFixture : IDisposable
{
    private const int PlacementReadVersion = 1;
    private const string AdministratorId = "root";
    private const string ReaderId = "event-reader";
    private const string TenantId = "tenant";
    private const string SystemTenantId = "system";
    private const string AllScopes = "*";
    private const string DatabaseId = "database";
    private const string TransactionDomainId = "orders";
    private const string PartitionKey = "customer-1";
    private const string DirectoryPrefix = "keyload-event-read-";
    private const string GuidFormat = "N";
    private const string FreshDirectoryRequired = "The fixture must own a fresh directory.";
    private const string CredentialSecret = "root.event-reader-test-credential-32-characters";
    private const string StreamSetName = "events";
    private const string StreamId = "stream-1";
    private const string EventType = "Created";
    private const string PrivateClassification = "private";
    private const string StreamHeadSpace = "stream-head";
    private const string EventSpace = "event";
    private const string SecretPath = "/secret";
    private const string HeaderSecretPath = "/secretHeader";
    internal const string ProtectedValue = "private-stream-value";
    internal const string ProtectedHeaderValue = "private-stream-header";
    internal const string PublicValue = "public-stream-value";
    private const string PayloadJson = "{\"secret\":\"" + ProtectedValue + "\",\"public\":\"" + PublicValue + "\"}";
    private const string HeadersJson = "{\"secretHeader\":\"" + ProtectedHeaderValue + "\",\"kind\":\"created\"}";
    private readonly string directory;
    private readonly TimeProvider clock;
    private readonly IOptions<EventSourceExecutionOptions> eventSourceOptions = UnitExecutionOptions.EventSource();
    private ZoneTreeStore store = null!;
    private DatabaseEngine database = null!;

    public StreamReadResourceFixture(int eventCount = 0, bool protectedFields = false, string? directory = null, TimeProvider? timeProvider = null)
    {
        var ownedDirectory = directory ?? Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        if (System.IO.Directory.Exists(ownedDirectory))
        {
            throw new ArgumentException(FreshDirectoryRequired, nameof(directory));
        }

        this.directory = ownedDirectory;
        clock = timeProvider ?? TimeProvider.System;
        try
        {
            Open(new());
            database.Bootstrap(new(AdministratorId, SystemTenantId, [new(AllScopes, AllScopes, Capability.All)], [AllScopes]) { ClusterAdministrator = true },
                DatabaseEngine.Credential(AdministratorId, AdministratorId, CredentialSecret));
            PhysicalShardTestBootstrap.Bootstrap(database, AdministratorId);
            ConfigureResource(protectedFields);
            ConfigureReader();
            AppendEvents(eventCount);
        }
        catch (Exception)
        {
            try
            {
                store?.Dispose();
            }
            finally
            {
                DeleteOwnedDirectory();
            }
            throw;
        }
    }

    public StreamPage Read(long afterRevision = 0, long generation = 1, int limit = 100, CancellationToken cancellationToken = default)
        => database.ReadStream(ReaderId, new(Partition, StreamSetName, StreamId, generation), afterRevision, limit, cancellationToken);

    internal StreamPage Traverse(ReadStreamRequest request, CancellationToken cancellationToken = default)
        => database.ReadStream(ReaderId, request, cancellationToken);

    internal ReadStreamRequest Traversal(StreamReadDirection direction = StreamReadDirection.Forward,
        int limit = 100, int maxBytes = 0, string? cursor = null, long after = 0)
        => new(new(Partition, StreamSetName, StreamId, 1), after, limit, direction, maxBytes, cursor);

    internal (long Position, string[] Image) Cut()
        => (store.Position, KeyLoad.UnitTests.Features.ClusterRouting.ControlledPartitionMovementRawImage.Bytes(store));

    internal void SetReader(bool allowed)
    {
        const int PolicyEpochStep = 1;
        var original = store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(ReaderId)))
            ?? throw new InvalidOperationException();
        Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(original with
        {
            Grants = allowed ? [new(Partition.DatabaseId, StreamSetName, Capability.EventsRead)] : [],
            PolicyEpoch = checked(original.PolicyEpoch + PolicyEpochStep)
        }));
    }

    internal void BindOriginalPlacement()
    {
        var original = database.ReadAtomicPartitionPlacement(AdministratorId, new(PlacementReadVersion, Partition));
        Submit(OperationKind.BindAtomicPartitionPlacement,
            new BindAtomicPartitionPlacementRequest(PlacementReadVersion, original.DirectoryRevision, Partition, original.PhysicalShardId));
    }

    internal AtomicPartitionPlacementResolution Placement
        => database.ReadAtomicPartitionPlacement(AdministratorId, new(PlacementReadVersion, Partition));

    internal StreamPage TraverseConfigured(ReadStreamRequest request, PhysicalShardRecord owner)
        => (StreamPage)new KeyLoad.Orleans.GrainCoreReadCapabilities(database).Execute(
            KeyLoad.Orleans.GrainReadKind.Stream, ReaderId, NativeSerialization.Serialize(request), default, owner)!;

    internal TimeSpan CursorLifetime => eventSourceOptions.Value.CursorLifetime;

    internal PartitionRef Partition { get; } = new(TenantId, DatabaseId, TransactionDomainId, PartitionKey);

    internal EventSourceHead EventSourceHead
        => database.ReadEventSource(AdministratorId,
            new(new(Partition, StreamSetName, EventSourceKind.Stream, StreamId))).Head;

    internal OperationResult Apply(ReplicatedOperation operation) => database.Apply(operation);

    public void Reopen(DatabaseLimits limits)
    {
        store.Dispose();
        Open(limits);
        PhysicalShardTestBootstrap.RequireExisting(database);
    }

    public long HeadAndFirstEventReadBytes()
    {
        var total = 0L;
        store.Read(view =>
        {
            view.ReadValue(HeadKey, static _ => { }, bytes => total += bytes);
            view.ReadValue(EventKey(1), static _ => { }, bytes => total += bytes);
            return true;
        });
        return total;
    }

    public void RetainFromRevision(long revision)
        => store.Commit((transaction, _) =>
        {
            var head = transaction.GetRecord<StreamHead>(HeadKey) ?? throw new InvalidOperationException();
            transaction.PutRecord(HeadKey, head with { FirstAvailableRevision = revision });
            return true;
        });

    public void Dispose()
    {
        try
        {
            store.Dispose();
        }
        finally
        {
            DeleteOwnedDirectory();
        }
    }

    private void DeleteOwnedDirectory()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private byte[] HeadKey => KeySpace.Partition(StreamHeadSpace, Partition, StreamSetName, StreamId);

    private byte[] EventKey(long revision)
        => KeySpace.Partition(EventSpace, Partition, StreamSetName, StreamId, 1, revision);

    private void Open(DatabaseLimits limits)
    {
        store = new(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution(), timeProvider: clock);
        database = new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), eventSourceOptions, UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance, clock);
    }

    private void ConfigureResource(bool protectedFields)
    {
        var definition = new ResourceDefinition(StreamSetName, ResourceKind.StreamSet, Partition.TransactionDomainId)
        {
            FieldPolicies = protectedFields ? [new(SecretPath, PrivateClassification)] : [],
            HeaderPolicies = protectedFields ? [new(HeaderSecretPath, PrivateClassification)] : []
        };
        Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, definition));
    }

    private void ConfigureReader()
    {
        var reader = new PrincipalRecord(ReaderId, Partition.TenantId,
            [new(Partition.DatabaseId, StreamSetName, Capability.EventsRead)], []);
        Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader));
    }

    private void AppendEvents(int eventCount)
    {
        if (eventCount == 0)
        {
            return;
        }
        var events = Enumerable.Range(1, eventCount)
            .Select(index => new EventData(index.ToString(System.Globalization.CultureInfo.InvariantCulture), EventType, PayloadJson, HeadersJson)).ToArray();
        var append = new AppendEvents(StreamSetName, StreamId, [.. events], ExpectedStreamRevision.NoStream);
        Submit(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), Partition, [append]));
    }

    private void Submit<T>(OperationKind kind, T payload)
    {
        var id = kind == OperationKind.Batch && payload is CommandRequest command ? command.CommandId : Guid.NewGuid();
        var operation = new ReplicatedOperation(id, kind, AdministratorId, TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(payload, JsonDefaults.Options));
        database.Apply(operation).Get<object>();
    }
}
