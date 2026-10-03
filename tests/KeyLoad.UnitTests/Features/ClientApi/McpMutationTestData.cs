using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>All twelve real public mutation DTOs, independent of catalog discovery.</summary>
internal static class McpMutationTestData
{
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
    private const string EnqueueKind = "enqueue";
    private const string UpsertEdgeKind = "upsertEdge";
    private const string DeleteEdgeKind = "deleteEdge";
    private const string SamplesKind = "appendSamples";
    private const string VectorKind = "putVector";
    private const string QueueToGraphKind = "queueToGraph";
    private const string GraphToQueueKind = "graphToQueue";
    internal static readonly ImmutableArray<string> Discriminators =
        [PutKind, PatchKindName, DeleteKind, AppendKind, PublishKind,
         EnqueueKind, UpsertEdgeKind, DeleteEdgeKind, SamplesKind, VectorKind,
         QueueToGraphKind, GraphToQueueKind];

    internal static ImmutableArray<Mutation> Create() =>
    [
        new PutDocument(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, McpCanonicalTestData.EmptyJson),
        new PatchDocument(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            [new FieldPatch(McpCanonicalTestData.Field, PatchKind.Remove)], Revision),
        new DeleteDocument(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity),
        new AppendEvents(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            [Event()], ExpectedStreamRevision.Any),
        new PublishTopic(McpCanonicalTestData.Resource, [Event()]),
        new EnqueueMessage(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, McpCanonicalTestData.EmptyJson),
        new UpsertEdge(McpCanonicalTestData.Resource, EdgeId, McpCanonicalTestData.Reference, McpCanonicalTestData.Reference, Label),
        new DeleteEdge(McpCanonicalTestData.Resource, EdgeId),
        new AppendSamples(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity,
            [new SampleData(EventId, DateTimeOffset.UnixEpoch, Sample)]),
        new PutVector(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, McpCanonicalTestData.Field,
            [Vector], new VectorSpace(SpaceId, Dimension, DistanceMetric.Cosine, Model, Version), Revision),
        new QueueToGraph(McpCanonicalTestData.Resource, McpCanonicalTestData.Resource, EdgeId),
        new GraphToQueueMutation(McpCanonicalTestData.Resource, McpCanonicalTestData.Resource,
            McpCanonicalTestData.Reference, EdgeId)
    ];

    private static EventData Event() => new(EventId, EventType, McpCanonicalTestData.EmptyJson);
}
