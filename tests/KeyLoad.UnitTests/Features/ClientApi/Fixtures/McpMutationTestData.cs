using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Canonical real public mutation DTOs, independent of catalog discovery.</summary>
internal static class McpMutationTestData
{
    private const string RefreshRollupKind = "refreshSampleRollup";
    private const string DropRollupKind = "dropSampleRollup";
    private const string EventId = "mcp-event";
    private const string EventType = "mcp-event-type";
    private const string EdgeId = "mcp-edge";
    private const string Label = "mcp-link";
    private const string SpaceId = "mcp-space";
    private const string Model = "mcp-model";
    private const string Version = "mcp-model-version";
    private const int Dimension = 1;
    private const long Revision = 1;
    private const double Sample = 1;
    private const float Vector = 1;
    private const string PutKind = "putDocument";
    private const string PatchKindName = "patchDocument";
    private const string DeleteKind = "deleteDocument";
    private const string AppendKind = "appendEvents";
    private const string PublishKind = "publishTopic";
    private const string PurgeKind = "purgeTopic";
    private const string EnqueueKind = "enqueue";
    private const string RedriveKind = "redriveQueueMessage";
    private const string CancelKind = "cancelQueueMessage";
    private const string ParkKind = "parkPendingQueueMessage";
    private const string UpsertEdgeKind = "upsertEdge";
    private const string DeleteEdgeKind = "deleteEdge";
    private const string ApplyReverseEdgeKind = "applyCrossPartitionReverseEdge";
    private const string CompleteReverseEdgeKind = "completeCrossPartitionReverseEdge";
    private const string TargetPartitionKey = "mcp-reverse-target";
    private const string SamplesKind = "appendSamples";
    private const string VectorKind = "putVector";
    private const string QueueToGraphKind = "queueToGraph";
    private const string GraphToQueueKind = "graphToQueue";
    private const string ExpireSamplesKind = "expireSamples";
    private const string StoreAggregateSnapshotKind = "storeAggregateSnapshot";
    private const string VectorProjectionKind = "applyVectorProjection";
    private const string CreateTransferKind = "createQueueTransfer";
    private const string AcceptTransferKind = "acceptQueueTransfer";
    private const string CompleteTransferKind = "completeQueueTransfer";
    private const string ConfigureScheduleKind = "configureRecurringSchedule";
    private const string EmitOccurrencesKind = "emitRecurringOccurrences";
    private const string CancelScheduleKind = "cancelRecurringSchedule";
    private const string CompareExchangeSagaKind = "compareExchangeSaga";
    private const string ExpireSagaKind = "expireSaga";
    private const string OpenChunkKind = "openSampleChunkWindow";
    private const string SealChunkKind = "sealSampleChunkWindow";
    private const string MergeChunkKind = "mergeSampleChunkWindow";
    private const string DropChunkKind = "dropSampleChunkWindow";
    internal static readonly ImmutableArray<string> Discriminators =
        [PutKind, PatchKindName, DeleteKind, AppendKind, PublishKind, PurgeKind,
         EnqueueKind, RedriveKind, CancelKind, ParkKind, UpsertEdgeKind, DeleteEdgeKind, ApplyReverseEdgeKind, CompleteReverseEdgeKind, SamplesKind, VectorKind,
         QueueToGraphKind, GraphToQueueKind, ExpireSamplesKind, RefreshRollupKind, DropRollupKind, StoreAggregateSnapshotKind,
         VectorProjectionKind, CreateTransferKind, AcceptTransferKind, CompleteTransferKind,
         ConfigureScheduleKind, EmitOccurrencesKind, CancelScheduleKind, CompareExchangeSagaKind, ExpireSagaKind,
         OpenChunkKind, SealChunkKind, MergeChunkKind, DropChunkKind];

    internal static ImmutableArray<Mutation> Create() =>
    [
        new PutDocument(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, McpCanonicalTestData.EmptyJson),
        new PatchDocument(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            [new FieldPatch(McpCanonicalTestData.Field, PatchKind.Remove)], Revision),
        new DeleteDocument(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity),
        new AppendEvents(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            [Event()], ExpectedStreamRevision.Any),
        new PublishTopic(McpCanonicalTestData.Resource, [Event()]),
        new PurgeTopic(McpCanonicalTestData.Resource, Revision),
        new EnqueueMessage(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, McpCanonicalTestData.EmptyJson),
        new RedriveQueueMessage(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, Revision, Revision),
        new CancelQueueMessage(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, Revision, Revision),
        new ParkPendingQueueMessage(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, Revision, Revision),
        new UpsertEdge(McpCanonicalTestData.Resource, EdgeId, McpCanonicalTestData.Reference, McpCanonicalTestData.Reference, Label),
        new DeleteEdge(McpCanonicalTestData.Resource, EdgeId),
        new ApplyCrossPartitionReverseEdge(McpCanonicalTestData.Partition, McpCanonicalTestData.Resource,
            EdgeId, ReverseDestination(), Revision),
        new CompleteCrossPartitionReverseEdge(McpCanonicalTestData.Partition, McpCanonicalTestData.Resource,
            EdgeId, ReverseDestination(), Revision),
        new AppendSamples(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            [new SampleData(EventId, DateTimeOffset.UnixEpoch, Sample)]),
        new PutVector(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, McpCanonicalTestData.Field,
            [Vector], new VectorSpace(SpaceId, Dimension, DistanceMetric.Cosine, Model, Version), Revision),
        new QueueToGraph(McpCanonicalTestData.Resource, McpCanonicalTestData.Resource, EdgeId),
        new GraphToQueueMutation(McpCanonicalTestData.Resource, McpCanonicalTestData.Resource,
            McpCanonicalTestData.Reference, EdgeId),
        new ExpireSamples(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            DateTimeOffset.UnixEpoch, SampleRetentionDefaults.DefaultDeletes),
        new StoreAggregateSnapshot(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            Revision, Model, Dimension, McpCanonicalTestData.EmptyJson, 0, Revision),
        new RefreshSampleRollup(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), 0),
        new DropSampleRollup(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), Revision),
        .. McpDerivedMutationTestData.Create(),
        new OpenSampleChunkWindow(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            McpCanonicalTestData.StableId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1)),
        new SealSampleChunkWindow(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            McpCanonicalTestData.StableId, Revision),
        new MergeSampleChunkWindow(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            McpCanonicalTestData.StableId, Revision),
        new DropSampleChunkWindow(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            McpCanonicalTestData.StableId, Revision)
    ];

    private static EventData Event() => new(EventId, EventType, McpCanonicalTestData.EmptyJson);

    private static EntityRef ReverseDestination() => McpCanonicalTestData.Reference with
    {
        Partition = McpCanonicalTestData.Partition with { PartitionKey = TargetPartitionKey }
    };
}
