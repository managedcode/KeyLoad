using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
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
    internal const string PurgeTopic = "purgeTopic";
    internal const string EnqueueMessage = "enqueue";
    internal const string UpsertEdge = "upsertEdge";
    internal const string DeleteEdge = "deleteEdge";
    internal const string ApplyCrossPartitionReverseEdge = "applyCrossPartitionReverseEdge";
    internal const string CompleteCrossPartitionReverseEdge = "completeCrossPartitionReverseEdge";
    internal const string AppendSamples = "appendSamples";
    internal const string ExpireSamples = "expireSamples";
    internal const string RefreshSampleRollup = SampleRollupProtocol.RefreshKind;
    internal const string DropSampleRollup = SampleRollupProtocol.DropKind;
    internal const string StoreAggregateSnapshot = "storeAggregateSnapshot";
    internal const string PutVector = "putVector";
    internal const string QueueToGraph = "queueToGraph";
    internal const string GraphToQueue = "graphToQueue";
    internal const string CreateQueueTransfer = "createQueueTransfer";
    internal const string AcceptQueueTransfer = "acceptQueueTransfer";
    internal const string CompleteQueueTransfer = "completeQueueTransfer";
    internal const string ApplyVectorProjection = "applyVectorProjection";
    internal const string ConfigureRecurringSchedule = "configureRecurringSchedule";
    internal const string EmitRecurringOccurrences = "emitRecurringOccurrences";
    internal const string CancelRecurringSchedule = "cancelRecurringSchedule";
    internal const string CompareExchangeSaga = "compareExchangeSaga";
    internal const string ExpireSaga = "expireSaga";
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
    private const string DefaultSafeDetail = "A KeyLoad operation failed.";
    /// <summary>Initializes a KeyLoad exception with the supplied domain code, safe detail, and HTTP status.</summary>
    /// <param name="code">Identifies the domain error.</param>
    /// <param name="safeDetail">Provides safe caller-visible detail.</param>
    /// <param name="statusCode">Provides the HTTP status associated with the error.</param>
    public KeyLoadException(ErrorCode code, string safeDetail, int statusCode)
        : this(code, safeDetail, statusCode, null)
    {
    }

    /// <summary>Initializes a validation exception with a standard safe detail.</summary>
    public KeyLoadException() : this(ErrorCode.Validation, DefaultSafeDetail, Errors.Status(ErrorCode.Validation), null)
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
    private const string ProblemTypePrefix = "urn:keyload:error:";
    /// <summary>Returns the HTTP status code associated with an error code.</summary>
    /// <param name="code">Specifies the code value.</param>
    /// <returns>The requested value.</returns>
    public static int Status(ErrorCode code) => code switch
    {
        ErrorCode.NotFound => (int)System.Net.HttpStatusCode.NotFound,
        ErrorCode.PermissionDenied => (int)System.Net.HttpStatusCode.Forbidden,
        ErrorCode.Unauthenticated => (int)System.Net.HttpStatusCode.Unauthorized,
        ErrorCode.Conflict or ErrorCode.RevisionConflict or ErrorCode.DuplicateEventId or ErrorCode.StaleLease => (int)System.Net.HttpStatusCode.Conflict,
        ErrorCode.ResourceExhausted or ErrorCode.BudgetExceeded => (int)System.Net.HttpStatusCode.TooManyRequests,
        ErrorCode.UnsupportedCapability => (int)System.Net.HttpStatusCode.UnprocessableEntity,
        ErrorCode.UnknownWriteOutcome or ErrorCode.RecoveryRequired or ErrorCode.OwnershipLost or ErrorCode.ClockUncertain => (int)System.Net.HttpStatusCode.ServiceUnavailable,
        _ => (int)System.Net.HttpStatusCode.BadRequest
    };
    /// <summary>Creates a problem detail with the standard status for an error code.</summary>
    /// <param name="code">Identifies the domain error code.</param>
    /// <param name="detail">Provides safe caller-visible error detail.</param>
    /// <returns>The standard problem details for the error code.</returns>
    public static Problem Problem(ErrorCode code, string detail) => new()
    {
        Type = $"{ProblemTypePrefix}{code}",
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

/// <summary>Identifies an atomic partition by tenant, database, transaction domain, and partition key.</summary>
/// <param name="TenantId">Identifies the owning tenant.</param>
/// <param name="DatabaseId">Identifies the database.</param>
/// <param name="TransactionDomainId">Identifies the transaction domain.</param>
/// <param name="PartitionKey">Provides the caller-visible partition key.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.PartitionRef)]
public sealed record PartitionRef([property: Orleans.Id(0)] string TenantId, [property: Orleans.Id(1)] string DatabaseId, [property: Orleans.Id(2)] string TransactionDomainId, [property: Orleans.Id(3)] string PartitionKey)
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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommitToken)]
public sealed record CommitToken([property: Orleans.Id(0)] Guid Incarnation, [property: Orleans.Id(1)] string AtomicPartitionId, [property: Orleans.Id(2)] long Position, [property: Orleans.Id(3)] long OwnershipEpoch);

/// <summary>Describes the resource effect produced by one committed mutation.</summary>
/// <param name="Kind">Identifies the mutation or trusted operation kind.</param>
/// <param name="Resource">Identifies the resource scope or mutation target.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.MutationReceipt)]
public sealed record MutationReceipt([property: Orleans.Id(0)] string Kind, [property: Orleans.Id(1)] string Resource, [property: Orleans.Id(2)] string Id, [property: Orleans.Id(3)] long Revision)
{
    private ImmutableArray<EntityRef> compositionReferences;

    [JsonIgnore]
    [Orleans.Id(4)]
    internal ImmutableArray<EntityRef> CompositionReferences
    {
        get => compositionReferences.IsDefault ? [] : compositionReferences;
        set => compositionReferences = value;
    }
}

/// <summary>Describes the durable result of a command and its mutation effects.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Token">Carries the token associated with this operation.</param>
/// <param name="Mutations">Lists committed mutation results or requested mutations.</param>
/// <param name="Durability">Identifies the achieved durability profile.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommitReceipt)]
public sealed record CommitReceipt([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] CommitToken Token, [property: Orleans.Id(2)] ImmutableArray<MutationReceipt> Mutations, [property: Orleans.Id(3)] DurabilityProfile Durability);

/// <summary>Records the replayable result of a command for its principal and fingerprint.</summary>
/// <param name="Fingerprint">Identifies the canonical command fingerprint.</param>
/// <param name="PrincipalId">Identifies the principal receiving the delivery.</param>
/// <param name="Receipt">Contains the committed command receipt when successful.</param>
/// <param name="Error">Contains the domain failure when rejected.</param>
/// <param name="SafeDetail">Contains safe caller-visible failure detail.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommandOutcome)]
public sealed record CommandOutcome([property: Orleans.Id(0)] string Fingerprint, [property: Orleans.Id(1)] string PrincipalId, [property: Orleans.Id(2)] CommitReceipt? Receipt, [property: Orleans.Id(3)] ErrorCode? Error, [property: Orleans.Id(4)] string? SafeDetail);

/// <summary>Defines one resource mutation included in a command.</summary>
/// <param name="Resource">Identifies the resource scope or mutation target.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = MutationDiscriminatorNames.Property)]
[JsonDerivedType(typeof(PutDocument), MutationDiscriminatorNames.PutDocument)]
[JsonDerivedType(typeof(PatchDocument), MutationDiscriminatorNames.PatchDocument)]
[JsonDerivedType(typeof(DeleteDocument), MutationDiscriminatorNames.DeleteDocument)]
[JsonDerivedType(typeof(AppendEvents), MutationDiscriminatorNames.AppendEvents)]
[JsonDerivedType(typeof(PublishTopic), MutationDiscriminatorNames.PublishTopic)]
[JsonDerivedType(typeof(PurgeTopic), MutationDiscriminatorNames.PurgeTopic)]
[JsonDerivedType(typeof(EnqueueMessage), MutationDiscriminatorNames.EnqueueMessage)]
[JsonDerivedType(typeof(UpsertEdge), MutationDiscriminatorNames.UpsertEdge)]
[JsonDerivedType(typeof(DeleteEdge), MutationDiscriminatorNames.DeleteEdge)]
[JsonDerivedType(typeof(ApplyCrossPartitionReverseEdge), MutationDiscriminatorNames.ApplyCrossPartitionReverseEdge)]
[JsonDerivedType(typeof(CompleteCrossPartitionReverseEdge), MutationDiscriminatorNames.CompleteCrossPartitionReverseEdge)]
[JsonDerivedType(typeof(AppendSamples), MutationDiscriminatorNames.AppendSamples)]
[JsonDerivedType(typeof(ExpireSamples), MutationDiscriminatorNames.ExpireSamples)]
[JsonDerivedType(typeof(RefreshSampleRollup), MutationDiscriminatorNames.RefreshSampleRollup)]
[JsonDerivedType(typeof(DropSampleRollup), MutationDiscriminatorNames.DropSampleRollup)]
[JsonDerivedType(typeof(StoreAggregateSnapshot), MutationDiscriminatorNames.StoreAggregateSnapshot)]
[JsonDerivedType(typeof(PutVector), MutationDiscriminatorNames.PutVector)]
[JsonDerivedType(typeof(QueueToGraph), MutationDiscriminatorNames.QueueToGraph)]
[JsonDerivedType(typeof(GraphToQueueMutation), MutationDiscriminatorNames.GraphToQueue)]
[JsonDerivedType(typeof(CreateQueueTransfer), MutationDiscriminatorNames.CreateQueueTransfer)]
[JsonDerivedType(typeof(AcceptQueueTransfer), MutationDiscriminatorNames.AcceptQueueTransfer)]
[JsonDerivedType(typeof(CompleteQueueTransfer), MutationDiscriminatorNames.CompleteQueueTransfer)]
[JsonDerivedType(typeof(ApplyVectorProjection), MutationDiscriminatorNames.ApplyVectorProjection)]
[JsonDerivedType(typeof(ConfigureRecurringSchedule), MutationDiscriminatorNames.ConfigureRecurringSchedule)]
[JsonDerivedType(typeof(EmitRecurringOccurrences), MutationDiscriminatorNames.EmitRecurringOccurrences)]
[JsonDerivedType(typeof(CancelRecurringSchedule), MutationDiscriminatorNames.CancelRecurringSchedule)]
[JsonDerivedType(typeof(CompareExchangeSaga), MutationDiscriminatorNames.CompareExchangeSaga)]
[JsonDerivedType(typeof(ExpireSaga), MutationDiscriminatorNames.ExpireSaga)]
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.Mutation)]
public abstract record Mutation([property: Orleans.Id(0)] string Resource);

/// <summary>Carries one identified command, its mutations, and partition ownership epoch.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="Mutations">Lists committed mutation results or requested mutations.</param>
/// <param name="OwnershipEpoch">Identifies the active ownership epoch.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommandRequest)]
public sealed record CommandRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] PartitionRef Partition, [property: Orleans.Id(2)] ImmutableArray<Mutation> Mutations, [property: Orleans.Id(3)] long OwnershipEpoch = CommandRequest.DefaultOwnershipEpoch)
{
    private const int DefaultOwnershipEpoch = 1;
}

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
    ReclaimBlob,
    /// <summary>Commits the initial physical shard placement catalog.</summary>
    BootstrapPhysicalShardCatalog,
    /// <summary>Commits an explicit atomic-partition assignment to its physical shard.</summary>
    BindAtomicPartitionPlacement,
    /// <summary>Changes private native runtime journal infrastructure through RF3.</summary>
    RuntimeJournal,
    /// <summary>Composes independent queue receives through request grains; never replicated as one command.</summary>
    ReceiveAcrossLanes,
    /// <summary>Commits server-confirmed physical owner registration at the stable control authority.</summary>
    RegisterPhysicalOwner,
    /// <summary>Runs administrator-only native ANN generation maintenance through independently authorized child requests.</summary>
    MaintainAnnIndex,
    /// <summary>Runs protected native text generation maintenance through independently authorized child requests.</summary>
    MaintainTextIndex
}

/// <summary>Carries a trusted operation and its evaluated principal and time.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Kind">Identifies the mutation or trusted operation kind.</param>
/// <param name="PrincipalId">Identifies the principal receiving the delivery.</param>
/// <param name="EvaluatedAt">Records when authorization evaluated the operation.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReplicatedOperation)]
public sealed record ReplicatedOperation([property: Orleans.Id(0)] Guid Id, [property: Orleans.Id(1)] OperationKind Kind, [property: Orleans.Id(2)] string PrincipalId, [property: Orleans.Id(3)] DateTimeOffset EvaluatedAt, [property: Orleans.Id(4)] string PayloadJson)
{
    /// <summary>Gets the native internal operation body after authorized public-ingress normalization.</summary>
    [Orleans.Id(5)]
    [JsonIgnore]
    public ReadOnlyMemory<byte> NativePayload { get; init; }
}

/// <summary>Carries a serialized operation result or a safe domain failure.</summary>
/// <param name="Json">Contains the JSON representation of the associated value.</param>
/// <param name="Error">Contains the domain failure when rejected.</param>
/// <param name="SafeDetail">Contains safe caller-visible failure detail.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.OperationResult)]
public sealed record OperationResult([property: Orleans.Id(0)] string? Json, [property: Orleans.Id(1)] ErrorCode? Error = null, [property: Orleans.Id(2)] string? SafeDetail = null)
{
    /// <summary>Gets the owned typed result for native internal dispatch.</summary>
    [Orleans.Id(3)]
    [JsonIgnore]
    public object? NativeValue { get; init; }

    private const string WrongNativeResultMessage = "The internal operation result type is invalid.";
    private const string RejectedOperationMessage = "The operation was rejected.";
    /// <summary>Deserializes the successful result or throws the stored domain failure.</summary>
    /// <typeparam name="T">Specifies the type of result.</typeparam>
    /// <returns>The requested value.</returns>
    public T Get<T>()
    {
        if (Error is { } code)
        {
            throw Errors.Fail(code, SafeDetail ?? RejectedOperationMessage);
        }
        if (NativeValue is T value)
        {
            return value;
        }
        throw Errors.Fail(ErrorCode.Corruption, WrongNativeResultMessage);
    }
}
