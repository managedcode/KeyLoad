using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Identifies a work queue in an atomic partition.</summary>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="Queue">Identifies the work queue.</param>
public sealed record QueueLaneRef(PartitionRef Partition, string Queue);

/// <summary>Defines storage, delivery, lease, and retry limits for a work queue.</summary>
public sealed record QueuePolicy
{
    /// <summary>Gets or sets the max attempts value.</summary>
    public int MaxAttempts { get; init; } = 5;
    /// <summary>Gets or sets the max stored messages value.</summary>
    public long MaxStoredMessages { get; init; } = 100_000;
    /// <summary>Gets or sets the max stored bytes value.</summary>
    public long MaxStoredBytes { get; init; } = 1_073_741_824;
    /// <summary>Gets or sets the max in flight messages value.</summary>
    public int MaxInFlightMessages { get; init; } = 1_000;
    /// <summary>Gets or sets the max in flight bytes value.</summary>
    public long MaxInFlightBytes { get; init; } = 67_108_864;
    /// <summary>Gets or sets the max lease seconds value.</summary>
    public int MaxLeaseSeconds { get; init; } = 300;
    /// <summary>Gets or sets the retry base milliseconds value.</summary>
    public int RetryBaseMilliseconds { get; init; } = 1_000;
    /// <summary>Gets or sets the retry max milliseconds value.</summary>
    public int RetryMaxMilliseconds { get; init; } = 300_000;
}

/// <summary>Adds a message to a work queue with scheduling and ordering metadata.</summary>
/// <param name="Queue">Identifies the work queue.</param>
/// <param name="MessageId">Identifies the queue message.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
/// <param name="NotBefore">Sets the optional earliest delivery time.</param>
/// <param name="ExpiresAt">Sets the optional expiration time.</param>
/// <param name="OrderingKey">Groups messages that must retain their ordering.</param>
public sealed record EnqueueMessage(string Queue, string MessageId, string PayloadJson, string HeadersJson = "{}",
    DateTimeOffset? NotBefore = null, DateTimeOffset? ExpiresAt = null, string? OrderingKey = null) : Mutation(Queue);

/// <summary>Identifies the persisted lifecycle state of a queued message.</summary>
public enum MessageState
{
    /// <summary>The message is waiting for its not-before time.</summary>
    Scheduled,
    /// <summary>The message is available for delivery.</summary>
    Ready,
    /// <summary>The message is currently leased to a consumer.</summary>
    Leased,
    /// <summary>The message was acknowledged.</summary>
    Acked,
    /// <summary>The message exceeded its retry policy.</summary>
    DeadLettered,
    /// <summary>The message was cancelled.</summary>
    Cancelled,
    /// <summary>The message expired before completion.</summary>
    Expired
}

/// <summary>Stores a queue message payload, headers, ordering key, and fingerprint.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
/// <param name="OrderingKey">Groups messages that must retain their ordering.</param>
/// <param name="Fingerprint">Identifies the canonical command fingerprint.</param>
public sealed record MessageBody(string Id, string PayloadJson, string HeadersJson, string? OrderingKey, string Fingerprint);

/// <summary>Stores queue delivery state, sequencing, lease, and failure metadata.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="State">Describes the expected stream state.</param>
/// <param name="Attempts">Specifies the attempts value.</param>
/// <param name="StateVersion">Specifies the state version value.</param>
/// <param name="ReadySequence">Specifies the ready sequence value.</param>
/// <param name="NotBefore">Sets the optional earliest delivery time.</param>
/// <param name="ExpiresAt">Sets the optional expiration time.</param>
/// <param name="LeaseOwner">Specifies the lease owner value.</param>
/// <param name="LeaseVersion">Identifies the lease version.</param>
/// <param name="LeaseUntil">Specifies the lease until value.</param>
/// <param name="DeliveryGeneration">Identifies the delivery generation.</param>
/// <param name="SafeFailureCode">Specifies the safe failure code value.</param>
public sealed record MessageMetadata(string Id, MessageState State, int Attempts, long StateVersion, long ReadySequence,
    DateTimeOffset? NotBefore, DateTimeOffset? ExpiresAt, string? LeaseOwner = null, long LeaseVersion = 0,
    DateTimeOffset? LeaseUntil = null, long DeliveryGeneration = 1, string? SafeFailureCode = null);

/// <summary>Reports queue storage and in-flight usage and the next ready sequence.</summary>
/// <param name="StoredMessages">Specifies the stored messages value.</param>
/// <param name="StoredBytes">Specifies the stored bytes value.</param>
/// <param name="InFlightMessages">Specifies the in flight messages value.</param>
/// <param name="InFlightBytes">Specifies the in flight bytes value.</param>
/// <param name="NextReadySequence">Specifies the next ready sequence value.</param>
public sealed record QueueCounters(long StoredMessages, long StoredBytes, long InFlightMessages, long InFlightBytes, long NextReadySequence);

/// <summary>Returns a leased message and the token and generation needed to process it.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
/// <param name="Token">Carries the delivery token.</param>
/// <param name="LeaseVersion">Identifies the lease version.</param>
/// <param name="LeaseUntil">Specifies the lease until value.</param>
/// <param name="Attempt">Specifies the attempt value.</param>
/// <param name="DeliveryGeneration">Identifies the delivery generation.</param>
public sealed record Delivery(string Id, string PayloadJson, string HeadersJson, string Token, long LeaseVersion,
    DateTimeOffset LeaseUntil, int Attempt, long DeliveryGeneration);

/// <summary>Requests a bounded number of messages from one queue lane.</summary>
/// <param name="RequestId">Identifies this receive request.</param>
/// <param name="Lane">Identifies the queue lane.</param>
/// <param name="MaxMessages">Limits deliveries returned by the request.</param>
/// <param name="MaxBytes">Limits returned payload bytes.</param>
/// <param name="LeaseSeconds">Requests the message lease duration.</param>
public sealed record ReceiveRequest(Guid RequestId, QueueLaneRef Lane, int MaxMessages = 1,
    int MaxBytes = 1_048_576, int LeaseSeconds = 30);

/// <summary>Returns leased deliveries and their committed receive token.</summary>
/// <param name="RequestId">Identifies this receive request.</param>
/// <param name="Deliveries">Contains leased messages.</param>
/// <param name="Token">Carries the delivery token.</param>
public sealed record ReceiveResult(Guid RequestId, ImmutableArray<Delivery> Deliveries, CommitToken Token);

/// <summary>Binds a delivery token to its lane, principal, lease, and incarnation.</summary>
/// <param name="Lane">Identifies the queue lane.</param>
/// <param name="MessageId">Identifies the queue message.</param>
/// <param name="PrincipalId">Identifies the principal receiving the delivery.</param>
/// <param name="LeaseVersion">Identifies the lease version.</param>
/// <param name="DeliveryGeneration">Identifies the delivery generation.</param>
/// <param name="Incarnation">Identifies the issuing storage incarnation.</param>
public sealed record DeliveryClaims(QueueLaneRef Lane, string MessageId, string PrincipalId, long LeaseVersion,
    long DeliveryGeneration, Guid Incarnation);

/// <summary>Selects the acknowledgement action for a delivery.</summary>
public enum DeliveryAction
{
    /// <summary>Acknowledge the delivery.</summary>
    Ack,
    /// <summary>Reject the delivery and apply retry policy.</summary>
    Nack,
    /// <summary>Extend the delivery lease.</summary>
    Renew
}

/// <summary>Requests acknowledgement, rejection, or lease renewal for a delivery.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Lane">Identifies the queue lane.</param>
/// <param name="Token">Carries the delivery token.</param>
/// <param name="Action">Selects the delivery action.</param>
/// <param name="LeaseSeconds">Requests the message lease duration.</param>
/// <param name="FailureCode">Provides a safe handler failure code.</param>
public sealed record DeliveryCommand(Guid CommandId, QueueLaneRef Lane, string Token, DeliveryAction Action,
    int? LeaseSeconds = null, string? FailureCode = null);

/// <summary>Requests an idempotent handler execution with its declared effects.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Lane">Identifies the queue lane.</param>
/// <param name="Token">Carries the delivery token.</param>
/// <param name="HandlerScope">Identifies the idempotent handler scope.</param>
/// <param name="ExecutionGeneration">Identifies the handler execution generation.</param>
/// <param name="Effects">Lists mutations committed with handler processing.</param>
public sealed record ProcessingRequest(Guid CommandId, QueueLaneRef Lane, string Token, string HandlerScope,
    long ExecutionGeneration, ImmutableArray<Mutation> Effects);

/// <summary>Returns message metadata with any requested payload and headers.</summary>
/// <param name="Metadata">Contains the message state metadata.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
public sealed record MessageInspection(MessageMetadata Metadata, string? PayloadJson, string? HeadersJson);
