using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Independent literal mixed-model input and expected mutation values for the movement operation.</summary>
internal static class ControlledPartitionMovementProcessLiteralCorpus
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
    internal const long InitialRevision = 1;
    private const int VectorDimension = 2;
    internal const string PrincipalId = "root";
    private const string Tenant = "system";
    private const string Application = "movement";
    private const string Group = "knowledge";
    private const string AtomicPartitionId = "partition-a";
    private const string VectorSpaceId = "movement-space";
    private const string VectorModel = "movement-model";
    private const string VectorVersion = "v1";
    private const string EventType = "knowledge.created";
    private const string PutDocumentKind = "putDocument";
    private const string PutVectorKind = "putVector";
    private const string PublishKind = "publishTopic";
    private const string EnqueueKind = "enqueue";
    internal static readonly Guid SeedId = Guid.Parse("a16d65d9-5472-4cb5-82a9-5c2ef95bf475");
    internal static readonly PartitionRef Partition = new(Tenant, Application, Group, AtomicPartitionId);
    internal static readonly VectorSpace Space = new(VectorSpaceId, VectorDimension,
        DistanceMetric.DotProduct, VectorModel, VectorVersion);

    internal const string BlobPartSha256 = "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a";
    internal static readonly Guid BlobUploadId = Guid.Parse("3836909a-2089-4e20-bbab-788a36df023c");
    internal static readonly Guid BlobCompleteId = Guid.Parse("7eeb58f9-989c-4a0a-9786-038ab9a8a1a7");
    internal static readonly BlobRef Blob = new(Partition, "movement-files", "evidence-1");
    internal static byte[] BlobBytes() => [1, 2, 3, 4];
    internal static byte[] PartialBlobBytes() => [2, 3];
    internal static string BlobIntegrityHash(Guid incarnation)
    {
        var bytes = BlobBytes();
        var initial = BlobIntegrity.InitialHash(incarnation, Blob, BlobUploadId, bytes.Length);
        return BlobIntegrity.NextHash(initial, 0, bytes.Length, BlobPartSha256);
    }

    internal static CommandRequest Seed() => new(SeedId, Partition,
    [
        new PutDocument(Collection, DocumentId, OriginalJson),
        new PutVector(Collection, DocumentId, VectorField, [1f, 0f], Space, InitialRevision),
        new PublishTopic(Topic, [new(EventId, EventType, EventJson, EmptyHeaders)]),
        new EnqueueMessage(Queue, MessageId, QueueJson, EmptyHeaders),
    ]);

    internal static ImmutableArray<MutationReceipt> ExpectedMutations() =>
    [
        new(PutDocumentKind, Collection, DocumentId, InitialRevision),
        new(PutVectorKind, Collection, DocumentId, InitialRevision),
        new(PublishKind, Topic, EventId, InitialRevision),
        new(EnqueueKind, Queue, MessageId, InitialRevision),
    ];

    internal static ReplicatedOperation SeedOperation(DatabaseEngine database, DateTimeOffset? evaluatedAt = null)
        => database.CreateNativeOperation(OperationKind.Batch, SeedId,
            PrincipalId, evaluatedAt ?? database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(Seed()));
}
