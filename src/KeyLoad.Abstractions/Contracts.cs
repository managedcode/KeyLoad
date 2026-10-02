using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLoad.Features.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication;

namespace KeyLoad;

internal static class MutationDiscriminatorNames
{
    internal const string Property = "kind";
    internal const string PutDocument = "putDocument";
    internal const string PatchDocument = "patchDocument";
    internal const string DeleteDocument = "deleteDocument";
    internal const string AppendEvents = "appendEvents";
    internal const string PublishTopic = "publishTopic";
    internal const string EnqueueMessage = "enqueue";
    internal const string UpsertEdge = "upsertEdge";
    internal const string DeleteEdge = "deleteEdge";
    internal const string AppendSamples = "appendSamples";
    internal const string PutVector = "putVector";
}

/// <summary>Identifies the stable error category returned by KeyLoad operations.</summary>
public enum ErrorCode
{
    /// <summary>The request or persisted value is invalid.</summary>
    Validation,
    /// <summary>The requested resource does not exist.</summary>
    NotFound,
    /// <summary>The requested change conflicts with current state.</summary>
    Conflict,
    /// <summary>The expected revision does not match current state.</summary>
    RevisionConflict,
    /// <summary>An event identifier is already present.</summary>
    DuplicateEventId,
    /// <summary>The authenticated principal is not permitted to perform the operation.</summary>
    PermissionDenied,
    /// <summary>The request has no valid authenticated identity.</summary>
    Unauthenticated,
    /// <summary>A configured resource capacity has been exhausted.</summary>
    ResourceExhausted,
    /// <summary>An operation exceeded its configured work budget.</summary>
    BudgetExceeded,
    /// <summary>The requested capability is not supported.</summary>
    UnsupportedCapability,
    /// <summary>The write outcome cannot be determined safely.</summary>
    UnknownWriteOutcome,
    /// <summary>Recovery must complete before the operation can proceed.</summary>
    RecoveryRequired,
    /// <summary>The supplied position token is no longer valid.</summary>
    TokenInvalidated,
    /// <summary>The supplied query cursor has expired.</summary>
    CursorExpired,
    /// <summary>The requested history is no longer available.</summary>
    HistoryUnavailable,
    /// <summary>The required index is not ready at the requested cut.</summary>
    IndexNotReady,
    /// <summary>The delivery lease has expired.</summary>
    LeaseExpired,
    /// <summary>The supplied delivery lease is stale.</summary>
    StaleLease,
    /// <summary>Dispatch is paused for the resource.</summary>
    DispatchPaused,
    /// <summary>This node no longer owns the resource.</summary>
    OwnershipLost,
    /// <summary>Persisted data is invalid or inconsistent.</summary>
    Corruption,
    /// <summary>The persisted format is not supported.</summary>
    FormatUnsupported,
    /// <summary>The clock cannot safely establish the required time.</summary>
    ClockUncertain,
    /// <summary>The operation was cancelled.</summary>
    Cancelled
}

/// <summary>Represents a safe, caller-visible KeyLoad failure with its domain code and HTTP status.</summary>
public sealed class KeyLoadException : Exception
{
    /// <summary>Initializes a KeyLoad exception with the supplied domain code, safe detail, and HTTP status.</summary>
    /// <param name="code">Identifies the domain error.</param>
    /// <param name="safeDetail">Provides safe caller-visible detail.</param>
    /// <param name="statusCode">Provides the HTTP status associated with the error.</param>
    public KeyLoadException(ErrorCode code, string safeDetail, int statusCode)
        : this(code, safeDetail, statusCode, null)
    {
    }

    /// <summary>Initializes a validation exception with a standard safe detail.</summary>
    public KeyLoadException() : this(ErrorCode.Validation, "A KeyLoad operation failed.", Errors.Status(ErrorCode.Validation), null)
    {
    }

    /// <summary>Initializes a validation exception with the supplied message.</summary>
    /// <param name="message">Provides safe caller-visible failure detail.</param>
    public KeyLoadException(string message) : this(ErrorCode.Validation, message, Errors.Status(ErrorCode.Validation), null)
    {
    }

    /// <summary>Initializes a validation exception with the supplied message and cause.</summary>
    /// <param name="message">Provides safe caller-visible failure detail.</param>
    /// <param name="innerException">Provides the exception that caused this failure.</param>
    public KeyLoadException(string message, Exception innerException)
        : this(ErrorCode.Validation, message, Errors.Status(ErrorCode.Validation), innerException)
    {
    }

    private KeyLoadException(ErrorCode code, string safeDetail, int statusCode, Exception? innerException)
        : base(safeDetail, innerException)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>Gets the domain error code.</summary>
    public ErrorCode Code { get; }

    /// <summary>Gets the corresponding HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>Creates the safe problem detail for this failure.</summary>
    /// <returns>The problem detail associated with this exception.</returns>
    public Problem ToProblem() => Errors.Problem(Code, Message);
}

/// <summary>Creates standard status mappings, problem details, and KeyLoad exceptions.</summary>
public static class Errors
{
    /// <summary>Returns the HTTP status code associated with an error code.</summary>
    /// <param name="code">Specifies the code value.</param>
    /// <returns>The requested value.</returns>
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
    /// <summary>Creates a problem detail with the standard status for an error code.</summary>
    /// <param name="code">Identifies the domain error code.</param>
    /// <param name="detail">Provides safe caller-visible error detail.</param>
    /// <returns>The standard problem details for the error code.</returns>
    public static Problem Problem(ErrorCode code, string detail) => new()
    {
        Type = $"urn:keyload:error:{code}",
        Title = code.ToString(),
        ErrorCode = code.ToString(),
        Detail = detail,
        StatusCode = Status(code)
    };
    /// <summary>Creates a KeyLoad exception with the status assigned to its error code.</summary>
    /// <param name="code">Identifies the domain error code.</param>
    /// <param name="detail">Provides safe caller-visible error detail.</param>
    /// <returns>A KeyLoad exception with the matching HTTP status.</returns>
    public static KeyLoadException Fail(ErrorCode code, string detail) => new(code, detail, Status(code));
}

/// <summary>Provides the canonical JSON options and persistence serialization helpers.</summary>
public static class JsonDefaults
{
    private const string MissingRecordMessage = "A persisted record has no value.";
    /// <summary>Gets the canonical serializer options.</summary>
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            MaxDepth = 64,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        };
        options.Converters.Add(new StrictByteMemoryJsonConverter());
        options.Converters.Add(new StrictImmutableArrayJsonConverterFactory());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
    /// <summary>Serializes a value using the canonical KeyLoad JSON options.</summary>
    /// <typeparam name="T">Specifies the value type.</typeparam>
    /// <param name="value">Provides the value to serialize.</param>
    /// <returns>The serialized UTF-8 JSON bytes.</returns>
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    /// <summary>Deserializes persisted JSON using the canonical options and rejects an empty result.</summary>
    /// <typeparam name="T">Specifies the type to deserialize.</typeparam>
    /// <param name="value">Provides the persisted UTF-8 JSON bytes.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="KeyLoadException">The persisted value is missing or corrupt.</exception>
    public static T Deserialize<T>(ReadOnlySpan<byte> value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw Errors.Fail(ErrorCode.Corruption, MissingRecordMessage);

    /// <summary>Deserializes canonical JSON text with a scoped, cleared UTF-8 input loan.</summary>
    /// <typeparam name="T">Specifies the type to deserialize.</typeparam>
    /// <param name="value">Provides JSON text with the existing UTF-8 replacement encoding semantics.</param>
    /// <returns>The deserialized value, owning any retained data independently of the input loan.</returns>
    /// <exception cref="ArgumentNullException">The JSON text argument is null.</exception>
    /// <exception cref="KeyLoadException">The serialized value is missing or corrupt.</exception>
    public static T Deserialize<T>(string value) => PooledJsonText.Deserialize<T>(value);
}

/// <summary>Identifies an atomic partition by tenant, database, transaction domain, and partition key.</summary>
/// <param name="TenantId">Identifies the owning tenant.</param>
/// <param name="DatabaseId">Identifies the database.</param>
/// <param name="TransactionDomainId">Identifies the transaction domain.</param>
/// <param name="PartitionKey">Provides the caller-visible partition key.</param>
public sealed record PartitionRef(string TenantId, string DatabaseId, string TransactionDomainId, string PartitionKey)
{
    /// <summary>Gets the stable hash of the partition identity.</summary>
    public string AtomicPartitionId => Convert.ToHexStringLower(SHA256.HashData(
        KeyCodec.Encode(TenantId, DatabaseId, TransactionDomainId, PartitionKey)));
}

/// <summary>Describes the durability guarantee recorded for a committed command.</summary>
public enum DurabilityProfile
{
    /// <summary>The write is buffered and has not reached a durable acknowledgement.</summary>
    Buffered,
    /// <summary>The write survives a process restart on the acknowledging node.</summary>
    ProcessDurable,
    /// <summary>The write has reached the local durable storage boundary.</summary>
    LocalDurable,
    /// <summary>A quorum acknowledged process-durable state.</summary>
    QuorumProcessDurable,
    /// <summary>A quorum acknowledged durable state.</summary>
    QuorumDurable
}

/// <summary>Identifies a committed position and ownership epoch for an atomic partition.</summary>
/// <param name="Incarnation">Identifies the issuing storage incarnation.</param>
/// <param name="AtomicPartitionId">Identifies the atomic partition.</param>
/// <param name="Position">Identifies the committed position.</param>
/// <param name="OwnershipEpoch">Identifies the active ownership epoch.</param>
public sealed record CommitToken(Guid Incarnation, string AtomicPartitionId, long Position, long OwnershipEpoch);

/// <summary>Describes the resource effect produced by one committed mutation.</summary>
/// <param name="Kind">Identifies the mutation or trusted operation kind.</param>
/// <param name="Resource">Identifies the resource scope or mutation target.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
public sealed record MutationReceipt(string Kind, string Resource, string Id, long Revision);

/// <summary>Describes the durable result of a command and its mutation effects.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Token">Carries the token associated with this operation.</param>
/// <param name="Mutations">Lists committed mutation results or requested mutations.</param>
/// <param name="Durability">Identifies the achieved durability profile.</param>
public sealed record CommitReceipt(Guid CommandId, CommitToken Token, ImmutableArray<MutationReceipt> Mutations, DurabilityProfile Durability);

/// <summary>Records the replayable result of a command for its principal and fingerprint.</summary>
/// <param name="Fingerprint">Identifies the canonical command fingerprint.</param>
/// <param name="PrincipalId">Identifies the principal receiving the delivery.</param>
/// <param name="Receipt">Contains the committed command receipt when successful.</param>
/// <param name="Error">Contains the domain failure when rejected.</param>
/// <param name="SafeDetail">Contains safe caller-visible failure detail.</param>
public sealed record CommandOutcome(string Fingerprint, string PrincipalId, CommitReceipt? Receipt, ErrorCode? Error, string? SafeDetail);

/// <summary>Defines server-side limits applied to database operations.</summary>
public sealed record DatabaseLimits
{
    /// <summary>Gets or sets the max document bytes value.</summary>
    public int MaxDocumentBytes { get; init; } = 1_048_576;
    /// <summary>Gets or sets the max json depth value.</summary>
    public int MaxJsonDepth { get; init; } = 32;
    /// <summary>Gets or sets the max batch mutations value.</summary>
    public int MaxBatchMutations { get; init; } = 256;
    /// <summary>Gets or sets the max batch bytes value.</summary>
    public int MaxBatchBytes { get; init; } = 8_388_608;
    /// <summary>Gets or sets the max scan records value.</summary>
    public int MaxScanRecords { get; init; } = 10_000;
    /// <summary>Gets or sets the max results value.</summary>
    public int MaxResults { get; init; } = 1_000;
    /// <summary>Gets or sets the writer queue capacity value.</summary>
    public int WriterQueueCapacity { get; init; } = 256;
    /// <summary>Gets or sets the max concurrent queries value.</summary>
    public int MaxConcurrentQueries { get; init; } = 16;
    /// <summary>Gets or sets the max query bytes value.</summary>
    public int MaxQueryBytes { get; init; } = 65_536;
    /// <summary>Gets or sets the max query depth value.</summary>
    public int MaxQueryDepth { get; init; } = 32;
    /// <summary>Gets or sets the max query tokens value.</summary>
    public int MaxQueryTokens { get; init; } = 2_048;
    /// <summary>Gets or sets the query deadline seconds value.</summary>
    public int QueryDeadlineSeconds { get; init; } = 30;
    /// <summary>Gets or sets the max query read bytes value.</summary>
    public long MaxQueryReadBytes { get; init; } = 67_108_864;
    /// <summary>Gets or sets the max search text tokens value.</summary>
    public long MaxSearchTextTokens { get; init; } = 1_048_576;
    /// <summary>Gets or sets the max outbox records value.</summary>
    public long MaxOutboxRecords { get; init; } = 100_000;
    /// <summary>Gets or sets the max outbox bytes value.</summary>
    public long MaxOutboxBytes { get; init; } = 1_073_741_824;
    /// <summary>Gets or sets the reserved outbox records value.</summary>
    public long ReservedOutboxRecords { get; init; } = 16_384;
    /// <summary>Gets or sets the reserved outbox bytes value.</summary>
    public long ReservedOutboxBytes { get; init; } = 2_147_483_648;
    /// <summary>Gets or sets the max projection consumers value.</summary>
    public int MaxProjectionConsumers { get; init; } = 64;
    /// <summary>Gets or sets the max projection batch bytes value.</summary>
    public int MaxProjectionBatchBytes { get; init; } = 16_777_216;
}

/// <summary>Defines one resource mutation included in a command.</summary>
/// <param name="Resource">Identifies the resource scope or mutation target.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = MutationDiscriminatorNames.Property)]
[JsonDerivedType(typeof(PutDocument), MutationDiscriminatorNames.PutDocument)]
[JsonDerivedType(typeof(PatchDocument), MutationDiscriminatorNames.PatchDocument)]
[JsonDerivedType(typeof(DeleteDocument), MutationDiscriminatorNames.DeleteDocument)]
[JsonDerivedType(typeof(AppendEvents), MutationDiscriminatorNames.AppendEvents)]
[JsonDerivedType(typeof(PublishTopic), MutationDiscriminatorNames.PublishTopic)]
[JsonDerivedType(typeof(EnqueueMessage), MutationDiscriminatorNames.EnqueueMessage)]
[JsonDerivedType(typeof(UpsertEdge), MutationDiscriminatorNames.UpsertEdge)]
[JsonDerivedType(typeof(DeleteEdge), MutationDiscriminatorNames.DeleteEdge)]
[JsonDerivedType(typeof(AppendSamples), MutationDiscriminatorNames.AppendSamples)]
[JsonDerivedType(typeof(PutVector), MutationDiscriminatorNames.PutVector)]
public abstract record Mutation(string Resource);

/// <summary>Carries one identified command, its mutations, and partition ownership epoch.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="Mutations">Lists committed mutation results or requested mutations.</param>
/// <param name="OwnershipEpoch">Identifies the active ownership epoch.</param>
public sealed record CommandRequest(Guid CommandId, PartitionRef Partition, ImmutableArray<Mutation> Mutations, long OwnershipEpoch = 1);

// Only the trusted server constructs this envelope. Caller roles and timestamps never come from public JSON.
/// <summary>Identifies a trusted operation recorded in the replicated command envelope.</summary>
public enum OperationKind
{
    /// <summary>The batch value.</summary>
    Batch,
    /// <summary>The receive value.</summary>
    Receive,
    /// <summary>The delivery value.</summary>
    Delivery,
    /// <summary>The processing value.</summary>
    Processing,
    /// <summary>The configure resource value.</summary>
    ConfigureResource,
    /// <summary>The configure principal value.</summary>
    ConfigurePrincipal,
    /// <summary>The configure api key value.</summary>
    ConfigureApiKey,
    /// <summary>The set dispatch value.</summary>
    SetDispatch,
    /// <summary>The membership value.</summary>
    Membership,
    /// <summary>The configure subscription value.</summary>
    ConfigureSubscription,
    /// <summary>The seek subscription value.</summary>
    SeekSubscription,
    /// <summary>The receive subscription value.</summary>
    ReceiveSubscription,
    /// <summary>The subscription delivery value.</summary>
    SubscriptionDelivery,
    /// <summary>The subscription processing value.</summary>
    SubscriptionProcessing,
    /// <summary>The set subscription paused value.</summary>
    SetSubscriptionPaused,
    /// <summary>The configure projection consumer value.</summary>
    ConfigureProjectionConsumer,
    /// <summary>The commit projection batch value.</summary>
    CommitProjectionBatch,
    /// <summary>The release projection consumer value.</summary>
    ReleaseProjectionConsumer,
    /// <summary>The purge outbox value.</summary>
    PurgeOutbox,
    /// <summary>Begins a reserved binary upload lifetime.</summary>
    BeginBlobUpload,
    /// <summary>Accepts one bounded binary part.</summary>
    WriteBlobPart,
    /// <summary>Publishes an accepted binary upload through revision CAS.</summary>
    CompleteBlobUpload,
    /// <summary>Aborts a binary upload and releases unused reservation.</summary>
    AbortBlobUpload,
    /// <summary>Publishes a binary object revision tombstone.</summary>
    DeleteBlob,
    /// <summary>Reclaims eligible binary parts through a bounded command.</summary>
    ReclaimBlob
}

/// <summary>Carries a trusted operation and its evaluated principal and time.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Kind">Identifies the mutation or trusted operation kind.</param>
/// <param name="PrincipalId">Identifies the principal receiving the delivery.</param>
/// <param name="EvaluatedAt">Records when authorization evaluated the operation.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
public sealed record ReplicatedOperation(Guid Id, OperationKind Kind, string PrincipalId, DateTimeOffset EvaluatedAt, string PayloadJson);

/// <summary>Carries a serialized operation result or a safe domain failure.</summary>
/// <param name="Json">Contains the JSON representation of the associated value.</param>
/// <param name="Error">Contains the domain failure when rejected.</param>
/// <param name="SafeDetail">Contains safe caller-visible failure detail.</param>
public sealed record OperationResult(string? Json, ErrorCode? Error = null, string? SafeDetail = null)
{
    private const string RejectedOperationMessage = "The operation was rejected.";
    private const string NullResultJson = "null";
    /// <summary>Deserializes the successful result or throws the stored domain failure.</summary>
    /// <typeparam name="T">Specifies the type of result.</typeparam>
    /// <returns>The requested value.</returns>
    public T Get<T>()
    {
        if (Error is { } code)
        {
            throw Errors.Fail(code, SafeDetail ?? RejectedOperationMessage);
        }
        return JsonDefaults.Deserialize<T>(Json ?? NullResultJson);
    }
}
