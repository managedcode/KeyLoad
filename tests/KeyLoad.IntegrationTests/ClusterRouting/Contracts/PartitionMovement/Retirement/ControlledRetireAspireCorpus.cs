using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Original literal mixed-model identities retained by the actual public Aspire movement operation.</summary>
internal static class ControlledRetireAspireCorpus
{
    internal const string Collection = "movement-documents";
    internal const string Topic = "movement-events";
    internal const string Queue = "movement-work";
    internal const string DocumentId = "knowledge-1";
    internal const string EventId = "event-1";
    internal const string MessageId = "message-1";
    internal const string VectorField = "/embedding";
    internal const string OriginalJson = "{\"text\":\"Київ knowledge\",\"state\":\"original\"}";
    internal const string EventJson = "{\"documentId\":\"knowledge-1\",\"kind\":\"seed\"}";
    internal const string QueueJson = "{\"documentId\":\"knowledge-1\",\"action\":\"inspect\"}";
    internal const string CanonicalQueueJson = "{\"action\":\"inspect\",\"documentId\":\"knowledge-1\"}";
    internal const string EmptyHeaders = "{}";
    internal const string EventType = "knowledge.created";
    internal const string BlobResource = "movement-files";
    internal const string BlobId = "evidence-1";
    internal const string BlobHash = "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a";
    internal const string HealthyId = "knowledge-followup";
    internal const string HealthyJson = "{\"text\":\"healthy\"}";
    internal const long Revision = 1L;
    internal const int Zero = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int BlobLength = 4;
    internal const float UnitVector = 1f;
    internal const float ZeroVector = 0f;
    internal const double SingleRank = 1.0 / 61.0;
    internal const string PutKind = "putDocument";
    internal const string VectorKind = "putVector";
    internal const string PublishKind = "publishTopic";
    internal const string EnqueueKind = "enqueue";
    private const string SpaceId = "movement-space";
    private const string Model = "movement-model";
    private const string ModelVersion = "v1";
    internal static readonly Guid SeedId = Guid.Parse("a16d65d9-5472-4cb5-82a9-5c2ef95bf475");
    internal static readonly Guid UploadId = Guid.Parse("3836909a-2089-4e20-bbab-788a36df023c");
    internal static readonly Guid CompleteId = Guid.Parse("7eeb58f9-989c-4a0a-9786-038ab9a8a1a7");
    internal static readonly Guid HealthyCommandId = Guid.Parse("2e9455b1-38d7-4c80-a56f-d5596e36b7ca");
    internal static readonly VectorSpace Space = new(SpaceId, Two, DistanceMetric.DotProduct, Model, ModelVersion);
    internal static byte[] BlobBytes => [1, 2, 3, 4];
    internal static byte[] PartialBytes => [2, 3];

    internal static CommandRequest Seed(PartitionRef partition) => new(SeedId, partition,
        [new PutDocument(Collection, DocumentId, OriginalJson),
         new PutVector(Collection, DocumentId, VectorField, [UnitVector, ZeroVector], Space, Revision),
         new PublishTopic(Topic, [new(EventId, EventType, EventJson, EmptyHeaders)]),
         new EnqueueMessage(Queue, MessageId, QueueJson, EmptyHeaders)]);

    internal static ImmutableArray<MutationReceipt> Mutations =>
        [new(PutKind, Collection, DocumentId, Revision), new(VectorKind, Collection, DocumentId, Revision),
         new(PublishKind, Topic, EventId, Revision), new(EnqueueKind, Queue, MessageId, Revision)];

    internal static CommandRequest Healthy(PartitionRef partition) => new(HealthyCommandId, partition,
        [new PutDocument(Collection, HealthyId, HealthyJson)]);
}
