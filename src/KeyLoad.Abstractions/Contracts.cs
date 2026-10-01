using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLoad.Storage;
using ManagedCode.Communication;

namespace KeyLoad;

public enum ErrorCode
{
    Validation, NotFound, Conflict, RevisionConflict, DuplicateEventId, PermissionDenied, Unauthenticated,
    ResourceExhausted, BudgetExceeded, UnsupportedCapability, UnknownWriteOutcome, RecoveryRequired,
    TokenInvalidated, CursorExpired, HistoryUnavailable, IndexNotReady, LeaseExpired, StaleLease,
    DispatchPaused, OwnershipLost, Corruption, FormatUnsupported, ClockUncertain, Cancelled
}

public sealed class KeyLoadException(ErrorCode code, string safeDetail, int statusCode) : Exception(safeDetail)
{
    public ErrorCode Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public Problem ToProblem() => Errors.Problem(Code, Message);
}

public static class Errors
{
    public static int Status(ErrorCode code) => code switch
    {
        ErrorCode.NotFound => 404,
        ErrorCode.PermissionDenied => 403,
        ErrorCode.Unauthenticated => 401,
        ErrorCode.Conflict or ErrorCode.RevisionConflict or ErrorCode.DuplicateEventId or ErrorCode.StaleLease => 409,
        ErrorCode.ResourceExhausted or ErrorCode.BudgetExceeded => 429,
        ErrorCode.UnsupportedCapability => 422,
        ErrorCode.UnknownWriteOutcome or ErrorCode.RecoveryRequired or ErrorCode.OwnershipLost or ErrorCode.ClockUncertain => 503,
        _ => 400
    };
    public static Problem Problem(ErrorCode code, string detail) => new()
    {
        Type = $"urn:keyload:error:{code}", Title = code.ToString(), ErrorCode = code.ToString(),
        Detail = detail, StatusCode = Status(code)
    };
    public static KeyLoadException Fail(ErrorCode code, string detail) => new(code, detail, Status(code));
}

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            MaxDepth = 64, PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    public static T Deserialize<T>(ReadOnlySpan<byte> value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw Errors.Fail(ErrorCode.Corruption, "A persisted record has no value.");
}

public sealed record PartitionRef(string TenantId, string DatabaseId, string TransactionDomainId, string PartitionKey)
{
    public string AtomicPartitionId => Convert.ToHexStringLower(SHA256.HashData(
        KeyCodec.Encode(TenantId, DatabaseId, TransactionDomainId, PartitionKey)));
}
public sealed record EntityRef(PartitionRef Partition, string Collection, string Id);
public sealed record StreamRef(PartitionRef Partition, string StreamSet, string StreamId, long Generation = 1);
public sealed record QueueLaneRef(PartitionRef Partition, string Queue);
public enum DurabilityProfile { Buffered, ProcessDurable, LocalDurable, QuorumProcessDurable, QuorumDurable }
public sealed record CommitToken(Guid Incarnation, string AtomicPartitionId, long Position, long OwnershipEpoch);
public sealed record MutationReceipt(string Kind, string Resource, string Id, long Revision);
public sealed record CommitReceipt(Guid CommandId, CommitToken Token, MutationReceipt[] Mutations, DurabilityProfile Durability);
public sealed record CommandOutcome(string Fingerprint, string PrincipalId, CommitReceipt? Receipt, ErrorCode? Error, string? SafeDetail);
public sealed record DatabaseLimits
{
    public int MaxDocumentBytes { get; init; } = 1_048_576;
    public int MaxJsonDepth { get; init; } = 32;
    public int MaxBatchMutations { get; init; } = 256;
    public int MaxBatchBytes { get; init; } = 8_388_608;
    public int MaxScanRecords { get; init; } = 10_000;
    public int MaxResults { get; init; } = 1_000;
    public int WriterQueueCapacity { get; init; } = 256;
    public int MaxConcurrentQueries { get; init; } = 16;
    public int MaxQueryBytes { get; init; } = 65_536;
    public int MaxQueryDepth { get; init; } = 32;
    public int MaxQueryTokens { get; init; } = 2_048;
    public int QueryDeadlineSeconds { get; init; } = 30;
}

public enum ResourceKind { Collection, StreamSet, WorkQueue, Topic, Graph, TimeSeries }
public enum DocumentAuthority { Document, EventStream }
public sealed record IndexDefinition(string Name, string[] Fields, bool Unique = false, bool IncludeNull = true, bool IncludeMissing = false);
public sealed record SensitiveFieldPolicy(string Path, string Classification, string RawReadGrant = "pii.read",
    string RawUseGrant = "pii.use", string WriteGrant = "pii.write", bool RequiredForProcessing = false);
public sealed record QueuePolicy
{
    public int MaxAttempts { get; init; } = 5;
    public long MaxStoredMessages { get; init; } = 100_000;
    public long MaxStoredBytes { get; init; } = 1_073_741_824;
    public int MaxInFlightMessages { get; init; } = 1_000;
    public long MaxInFlightBytes { get; init; } = 67_108_864;
    public int MaxLeaseSeconds { get; init; } = 300;
    public int RetryBaseMilliseconds { get; init; } = 1_000;
    public int RetryMaxMilliseconds { get; init; } = 300_000;
}
public sealed record ResourceDefinition(string Name, ResourceKind Kind, string TransactionDomainId)
{
    public IndexDefinition[] Indexes { get; init; } = [];
    public SensitiveFieldPolicy[] FieldPolicies { get; init; } = [];
    public SensitiveFieldPolicy[] HeaderPolicies { get; init; } = [];
    public QueuePolicy QueuePolicy { get; init; } = new();
    public EventRetentionPolicy EventRetention { get; init; } = new();
    public DocumentAuthority Authority { get; init; }
    public long SchemaVersion { get; init; } = 1;
    public bool Paused { get; init; }
}
[Flags]
public enum Capability : long
{
    None = 0, DocumentsRead = 1L << 0, DocumentsWrite = 1L << 1, Query = 1L << 2,
    EventsAppend = 1L << 3, EventsRead = 1L << 4, QueuePublish = 1L << 5, QueueConsume = 1L << 6,
    QueueAck = 1L << 7, QueueRenew = 1L << 8, QueueInspect = 1L << 9, DeadLettersRead = 1L << 10,
    DeadLettersRedrive = 1L << 11, SubscriptionsManage = 1L << 12, GraphRead = 1L << 13,
    GraphWrite = 1L << 14, SeriesRead = 1L << 15, SeriesAppend = 1L << 16, VectorSearch = 1L << 17,
    VectorRaw = 1L << 18, SchemaManage = 1L << 19, SecurityManage = 1L << 20, BackupManage = 1L << 21,
    BackupRestore = 1L << 22, Diagnose = 1L << 23, DataExport = 1L << 24,
    TopicsPublish = 1L << 25, TopicsRead = 1L << 26, SubscriptionsConsume = 1L << 27,
    SubscriptionsAck = 1L << 28, All = (1L << 29) - 1
}
public sealed record ScopeGrant(string Database, string Resource, Capability Capabilities);
public sealed record PrincipalRecord(string Id, string TenantId, ScopeGrant[] Grants, string[] FieldGrants)
{
    public bool ClusterAdministrator { get; init; }
    public string? OwnerId { get; init; }
    public string[] Projects { get; init; } = [];
    public bool RestrictRows { get; init; }
    public bool Revoked { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public long PolicyEpoch { get; init; } = 1;
}
public sealed record RowAccess(string? OwnerId = null, string? ProjectId = null);
public sealed record ApiKeyRecord(string Id, string PrincipalId, string Verifier, DateTimeOffset? ExpiresAt = null, bool Revoked = false);
public sealed record ApiKeyCreated(string Id, string Secret);
public sealed record DocumentRecord(EntityRef Reference, long Revision, string Json, RowAccess Access,
    DateTimeOffset UpdatedAt, bool Deleted = false);
public sealed record DocumentResult(EntityRef Reference, long Revision, string Json, bool Redacted, string[] RedactedFields);
public sealed record StreamHead(long TailRevision, long FirstAvailableRevision, long Generation);
public sealed record EventRecord(StreamRef Stream, long Revision, long EventSequence, EventData Data, DateTimeOffset RecordedAt);
public sealed record StreamPage(StreamRef Stream, StreamHead Head, EventRecord[] Events, long CutPosition, bool HasMore);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PutDocument), "putDocument")]
[JsonDerivedType(typeof(PatchDocument), "patchDocument")]
[JsonDerivedType(typeof(DeleteDocument), "deleteDocument")]
[JsonDerivedType(typeof(AppendEvents), "appendEvents")]
[JsonDerivedType(typeof(PublishTopic), "publishTopic")]
[JsonDerivedType(typeof(EnqueueMessage), "enqueue")]
[JsonDerivedType(typeof(UpsertEdge), "upsertEdge")]
[JsonDerivedType(typeof(DeleteEdge), "deleteEdge")]
[JsonDerivedType(typeof(AppendSamples), "appendSamples")]
[JsonDerivedType(typeof(PutVector), "putVector")]
public abstract record Mutation(string Resource);
public sealed record PutDocument(string Collection, string Id, string Json, long? ExpectedRevision = null,
    RowAccess? Access = null, bool ExplicitReplacement = false) : Mutation(Collection);
public enum PatchKind { Set, Remove }
public sealed record FieldPatch(string Path, PatchKind Kind, string? ValueJson = null);
public sealed record PatchDocument(string Collection, string Id, FieldPatch[] Patches, long ExpectedRevision) : Mutation(Collection);
public sealed record DeleteDocument(string Collection, string Id, long? ExpectedRevision = null) : Mutation(Collection);
public enum ExpectedStreamState { Exact, NoStream, Any }
public sealed record ExpectedStreamRevision(ExpectedStreamState State, long Revision = 0)
{
    public static ExpectedStreamRevision Exact(long revision) => new(ExpectedStreamState.Exact, revision);
    public static ExpectedStreamRevision NoStream { get; } = new(ExpectedStreamState.NoStream);
    public static ExpectedStreamRevision Any { get; } = new(ExpectedStreamState.Any);
}
public sealed record EventData(string EventId, string EventType, string PayloadJson, string HeadersJson = "{}",
    int SchemaVersion = 1, DateTimeOffset? OccurredAt = null, string? CorrelationId = null, string? CausationId = null);
public sealed record AppendEvents(string StreamSet, string StreamId, EventData[] Events,
    ExpectedStreamRevision ExpectedRevision, long Generation = 1) : Mutation(StreamSet);
public sealed record EnqueueMessage(string Queue, string MessageId, string PayloadJson, string HeadersJson = "{}",
    DateTimeOffset? NotBefore = null, DateTimeOffset? ExpiresAt = null, string? OrderingKey = null) : Mutation(Queue);
public sealed record UpsertEdge(string Graph, string EdgeId, EntityRef From, EntityRef To, string Label,
    string AttributesJson = "{}", long? ExpectedRevision = null) : Mutation(Graph);
public sealed record DeleteEdge(string Graph, string EdgeId, long? ExpectedRevision = null) : Mutation(Graph);
public sealed record SampleData(string EventId, DateTimeOffset Timestamp, double Value);
public sealed record AppendSamples(string SeriesSet, string SeriesId, SampleData[] Samples, string TagsJson = "{}") : Mutation(SeriesSet);
public enum DistanceMetric { Cosine, Euclidean, DotProduct }
public sealed record VectorSpace(string Id, int Dimension, DistanceMetric Metric, string Model, string Version);
public sealed record PutVector(string Collection, string Id, string Field, float[] Values, VectorSpace Space,
    long ExpectedDocumentRevision) : Mutation(Collection);
public sealed record CommandRequest(Guid CommandId, PartitionRef Partition, Mutation[] Mutations, long OwnershipEpoch = 1);

public enum MessageState { Scheduled, Ready, Leased, Acked, DeadLettered, Cancelled, Expired }
public sealed record MessageBody(string Id, string PayloadJson, string HeadersJson, string? OrderingKey, string Fingerprint);
public sealed record MessageMetadata(string Id, MessageState State, int Attempts, long StateVersion, long ReadySequence,
    DateTimeOffset? NotBefore, DateTimeOffset? ExpiresAt, string? LeaseOwner = null, long LeaseVersion = 0,
    DateTimeOffset? LeaseUntil = null, long DeliveryGeneration = 1, string? SafeFailureCode = null);
public sealed record QueueCounters(long StoredMessages, long StoredBytes, long InFlightMessages, long InFlightBytes, long NextReadySequence);
public sealed record Delivery(string Id, string PayloadJson, string HeadersJson, string Token, long LeaseVersion,
    DateTimeOffset LeaseUntil, int Attempt, long DeliveryGeneration);
public sealed record ReceiveRequest(Guid RequestId, QueueLaneRef Lane, int MaxMessages = 1,
    int MaxBytes = 1_048_576, int LeaseSeconds = 30);
public sealed record ReceiveResult(Guid RequestId, Delivery[] Deliveries, CommitToken Token);
public sealed record DeliveryClaims(QueueLaneRef Lane, string MessageId, string PrincipalId, long LeaseVersion,
    long DeliveryGeneration, Guid Incarnation);
public enum DeliveryAction { Ack, Nack, Renew }
public sealed record DeliveryCommand(Guid CommandId, QueueLaneRef Lane, string Token, DeliveryAction Action,
    int? LeaseSeconds = null, string? FailureCode = null);
public sealed record ProcessingRequest(Guid CommandId, QueueLaneRef Lane, string Token, string HandlerScope,
    long ExecutionGeneration, Mutation[] Effects);
public sealed record MessageInspection(MessageMetadata Metadata, string? PayloadJson, string? HeadersJson);
public sealed record EdgeRecord(string Id, EntityRef From, EntityRef To, string Label, string AttributesJson, long Revision);
public sealed record SampleRecord(string SeriesId, SampleData Sample, long Sequence, string TagsJson);
public sealed record VectorRecord(string DocumentId, string Field, VectorSpace Space, float[] Values, long DocumentRevision);
public sealed record RankedDocument(DocumentResult Document, double Score);

// Only the trusted server constructs this envelope. Caller roles and timestamps never come from public JSON.
public enum OperationKind { Batch, Receive, Delivery, Processing, ConfigureResource, ConfigurePrincipal, ConfigureApiKey, SetDispatch, Membership,
    ConfigureSubscription, SeekSubscription, ReceiveSubscription, SubscriptionDelivery, SubscriptionProcessing, SetSubscriptionPaused }
public sealed record ReplicatedOperation(Guid Id, OperationKind Kind, string PrincipalId, DateTimeOffset EvaluatedAt, string PayloadJson);
public sealed record OperationResult(string? Json, ErrorCode? Error = null, string? SafeDetail = null)
{
    public T Get<T>()
    {
        if (Error is { } code) throw Errors.Fail(code, SafeDetail ?? "The operation was rejected.");
        return JsonDefaults.Deserialize<T>(System.Text.Encoding.UTF8.GetBytes(Json ?? "null"));
    }
}
public sealed record ConfigureResourceRequest(string TenantId, string DatabaseId, ResourceDefinition Definition);
public sealed record ConfigurePrincipalRequest(PrincipalRecord Principal);
public sealed record ConfigureApiKeyRequest(ApiKeyRecord ApiKey);
