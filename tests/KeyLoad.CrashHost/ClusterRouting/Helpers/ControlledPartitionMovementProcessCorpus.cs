using KeyLoad.Core;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Original mixed-model input for actual movement child journal operations.</summary>
internal static class ControlledPartitionMovementProcessCorpus
{
    private const string SeedIdText = "a16d65d9-5472-4cb5-82a9-5c2ef95bf475";
    private const string PartitionTenant = "system";
    private const string PartitionDatabase = "movement";
    private const string PartitionDomain = "knowledge";
    private const string PartitionKey = "partition-a";
    private const float VectorFirstComponent = 1f;
    private const float VectorSecondComponent = 0f;
    /// <summary>The original document resource.</summary>
    public const string Collection = "movement-documents";
    /// <summary>The original event topic.</summary>
    public const string Topic = "movement-events";
    /// <summary>The original work queue.</summary>
    public const string Queue = "movement-work";
    private const string DocumentId = "knowledge-1";
    private const string EventId = "event-1";
    private const string MessageId = "message-1";
    private const string VectorField = "/embedding";
    private const string DocumentJson = "{\"text\":\"Київ knowledge\",\"state\":\"original\"}";
    private const string EventJson = "{\"documentId\":\"knowledge-1\",\"kind\":\"seed\"}";
    private const string QueueJson = "{\"documentId\":\"knowledge-1\",\"action\":\"inspect\"}";
    private const string Headers = "{}";
    private const string EventType = "knowledge.created";
    private const string PrincipalId = "root";
    private const long OriginalRevision = 1;
    private const int Dimension = 2;
    private const string SpaceId = "movement-space";
    private const string ModelId = "movement-model";
    private const string ModelVersion = "v1";
    /// <summary>The immutable original mixed batch command identity.</summary>
    public static readonly Guid SeedId = Guid.Parse(SeedIdText);
    /// <summary>The original full logical atomic partition identity.</summary>
    public static readonly PartitionRef Partition = new(PartitionTenant, PartitionDatabase, PartitionDomain, PartitionKey);

    /// <summary>Creates the unchanged original mixed input, never a verified movement permit.</summary>
    public static CommandRequest Seed() => new(SeedId, Partition,
    [
        new PutDocument(Collection, DocumentId, DocumentJson),
        new PutVector(Collection, DocumentId, VectorField, [VectorFirstComponent, VectorSecondComponent],
            new(SpaceId, Dimension, DistanceMetric.DotProduct, ModelId, ModelVersion), OriginalRevision),
        new PublishTopic(Topic, [new(EventId, EventType, EventJson, Headers)]),
        new EnqueueMessage(Queue, MessageId, QueueJson, Headers),
    ]);

    /// <summary>Creates the actual native batch to append through the original child journal owner.</summary>
    /// <param name="database">The borrowed actual canonical engine.</param>
    /// <param name="evaluatedAt">The original trusted operation evaluation time.</param>
    public static ReplicatedOperation SeedOperation(DatabaseEngine database, DateTimeOffset evaluatedAt)
        => database.CreateNativeOperation(OperationKind.Batch, SeedId, PrincipalId, evaluatedAt,
            NativeSerialization.Serialize(Seed()));
}
