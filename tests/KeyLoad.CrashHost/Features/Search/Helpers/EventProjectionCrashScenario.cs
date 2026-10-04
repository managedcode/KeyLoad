using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.Search;

internal static class EventProjectionCrashScenario
{
    internal const string Mode = "event-projection-process";
    internal const string OperationFile = "event-projection-operation.bin";
    internal const string PositionFile = "event-projection-before-position.bin";
    internal const string OutboxHeadFile = "event-projection-before-outbox-head.bin";
    internal const string SourceDocumentFile = "event-projection-before-source-document.bin";
    internal const string TargetDocumentFile = "event-projection-before-target-document.bin";
    internal const string StreamHeadFile = "event-projection-before-stream-head.bin";
    internal const string EventRecordFile = "event-projection-before-event.bin";
    internal const string EventIdentityFile = "event-projection-before-event-identity.bin";
    internal const string StreamSet = "projection-recovery-events";
    internal const string StreamId = "projection-recovery-source";
    internal const string Collection = "projection-recovery-records";
    internal const string SourceId = "projection-source";
    internal const string TargetId = "projection-derived";
    internal const string BaselineId = "projection-baseline";
    internal const string EventId = "projection-source-event";
    internal const string EventType = "SourceChanged";
    internal const string InputField = "/input";
    internal const string VectorField = "/embedding";
    internal const string ReducerId = "projection-recovery-reducer";
    internal const string ReducerVersion = "v1";
    internal const string MutationKind = "applyVectorProjection";
    internal const string VectorKeySpace = "vector";
    internal const string LineageKeySpace = "vector-projection-lineage";
    internal const string EffectKeySpace = "vector-projection-effect";
    internal const string StreamHeadKeySpace = "stream-head";
    internal const string EventKeySpace = "event";
    internal const string EventIdentityKeySpace = "event-id";
    internal const string OutboxHeadKeySpace = "outbox-head";
    internal const string OutboxKeySpace = "outbox";
    internal const int Generation = 1;
    internal const long EventRevision = 1;
    internal const long DocumentRevision = 1;
    internal const string EventPayloadJson = "{\"source\":\"projection-event-private-canary\"}";
    internal const string SourceDocumentJson = "{\"input\":\"projection-document-private-canary\"}";
    internal const string UpdatedSourceDocumentJson = "{\"input\":\"updated source value\"}";
    internal const string TargetDocumentJson = "{\"kind\":\"derived\"}";
    internal const string BaselineDocumentJson = "{\"kind\":\"baseline\"}";
    internal const string SourceSeedCommandId = "eaa2e6a5-9540-4b23-9549-bb6261c49695";
    internal const string CollectionCommandId = "2e64ece2-b1b4-4d8f-9da8-69e54f9ef330";
    internal const string StreamCommandId = "4f942e42-a849-46a2-b271-4b9b62f50d74";
    internal const string ProjectionCommandId = "8758386c-bd83-4d0b-99df-b9ce58ee3e0d";
    internal const string SourceChangeCommandId = "338f8ac3-bbab-4f17-b1c5-1b74d26a94e2";
    internal const string SpaceId = "projection-recovery-space";
    internal const string SpaceModel = "projection-recovery-model";
    internal const string SpaceVersion = "v1";
    internal const string SourcePrivateValue = "projection-document-private-canary";
    internal const string EventPrivateValue = "projection-event-private-canary";
    internal const int SpaceDimension = 2;
    internal static PartitionRef Partition { get; } = new(CrashFixtureValues.Tenant,
        CrashFixtureValues.Database, CrashFixtureValues.Orders, CrashFixtureValues.Partition);
    internal static VectorSpace Space { get; } = new(SpaceId, SpaceDimension, DistanceMetric.Cosine,
        SpaceModel, SpaceVersion);
    internal static StreamRef Stream { get; } = new(Partition, StreamSet, StreamId, Generation);

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store);
        Seed(database);
        var request = ProjectionRequest();
        var commandId = Guid.Parse(ProjectionCommandId);
        var command = new CommandRequest(commandId, Partition, [request]);
        var operation = CrashDatabase.Operation(OperationKind.Batch, command, commandId);
        await PreserveBeforeCutAsync(directory, store, operation);
        boundary.Position = store.Position + 1;
        boundary.Armed = true;
        _ = database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    internal static ApplyVectorProjection ProjectionRequest()
        => new(Stream, EventRevision, EventId, new(Partition, Collection, SourceId), DocumentRevision,
            InputField, ReducerId, ReducerVersion, Generation,
            new(Collection, TargetId, VectorField, [1, 0], Space, DocumentRevision));

    internal static byte[] DocumentKey(string id)
        => DocumentStorageKeys.RecordKey(Partition, Collection, id);

    internal static byte[] VectorKey(string id)
        => KeySpace.Partition(VectorKeySpace, Partition, Collection, VectorField, id);

    internal static byte[] LineageKey()
        => KeySpace.Partition(LineageKeySpace, Partition, Collection, VectorField, TargetId);

    internal static byte[] EffectKey(ApplyVectorProjection request)
        => KeySpace.Partition(EffectKeySpace, Partition, request.SourceStream.StreamSet,
            request.SourceStream.StreamId, request.SourceStream.Generation, request.SourceEventRevision,
            request.SourceEventId, request.SourceDocument.Collection, request.SourceDocument.Id,
            request.SourceDocumentRevision, request.InputField, request.ReducerId, request.ReducerVersion,
            request.ReducerGeneration, request.Target.Collection, request.Target.Id, request.Target.Field,
            request.Target.Space.Id, request.Target.Space.Dimension, (int)request.Target.Space.Metric,
            request.Target.Space.Model, request.Target.Space.Version);

    internal static byte[] StreamHeadKey()
        => KeySpace.Partition(StreamHeadKeySpace, Partition, StreamSet, StreamId);

    internal static byte[] EventKey()
        => KeySpace.Partition(EventKeySpace, Partition, StreamSet, StreamId, Generation, EventRevision);

    internal static byte[] EventIdentityKey()
        => KeySpace.Partition(EventIdentityKeySpace, Partition, StreamSet, StreamId, Generation, EventId);

    internal static byte[] OutboxHeadKey()
        => KeySpace.Partition(OutboxHeadKeySpace, Partition);

    internal static byte[] OutboxEntryKey(long sequence)
        => KeySpace.Partition(OutboxKeySpace, Partition, sequence);

    private static void Seed(DatabaseEngine database)
    {
        CrashDatabase.Submit(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId,
                new(Collection, ResourceKind.Collection, Partition.TransactionDomainId)),
            Guid.Parse(CollectionCommandId)).Get<ResourceDefinition>();
        CrashDatabase.Submit(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId,
                new(StreamSet, ResourceKind.StreamSet, Partition.TransactionDomainId)),
            Guid.Parse(StreamCommandId)).Get<ResourceDefinition>();
        var commandId = Guid.Parse(SourceSeedCommandId);
        var command = new CommandRequest(commandId, Partition,
        [
            new PutDocument(Collection, SourceId, SourceDocumentJson),
            new PutDocument(Collection, TargetId, TargetDocumentJson),
            new PutDocument(Collection, BaselineId, BaselineDocumentJson),
            new PutVector(Collection, BaselineId, VectorField, [0, 1], Space, DocumentRevision),
            new AppendEvents(StreamSet, StreamId, [new(EventId, EventType, EventPayloadJson)],
                ExpectedStreamRevision.NoStream, Generation)
        ]);
        _ = CrashDatabase.Submit(database, OperationKind.Batch, command, commandId).Get<CommitReceipt>();
    }

    private static async Task PreserveBeforeCutAsync(string directory, ZoneTreeStore store,
        ReplicatedOperation operation)
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, OperationFile), NativeSerialization.Serialize(operation));
        await File.WriteAllBytesAsync(Path.Combine(directory, PositionFile), NativeSerialization.Serialize(store.Position));
        await SaveRecordAsync(directory, store, OutboxHeadFile, OutboxHeadKey());
        await SaveRecordAsync(directory, store, SourceDocumentFile, DocumentKey(SourceId));
        await SaveRecordAsync(directory, store, TargetDocumentFile, DocumentKey(TargetId));
        await SaveRecordAsync(directory, store, StreamHeadFile, StreamHeadKey());
        await SaveRecordAsync(directory, store, EventRecordFile, EventKey());
        await SaveRecordAsync(directory, store, EventIdentityFile, EventIdentityKey());
        var initialVector = store.Read(view => view.ReadOwnedValue(VectorKey(TargetId)));
        if (initialVector is not null)
        {
            throw new InvalidOperationException("The projection target unexpectedly had a vector before the crash cut.");
        }
    }

    private static async Task SaveRecordAsync(string directory, ZoneTreeStore store, string fileName, byte[] key)
    {
        var value = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("A required canonical seed record was missing.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), value);
    }
}
