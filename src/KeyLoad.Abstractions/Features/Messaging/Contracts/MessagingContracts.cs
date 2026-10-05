using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Identifies a work queue in an atomic partition.</summary>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="Queue">Identifies the work queue.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueueLaneRef)]
public sealed record QueueLaneRef([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Queue);

/// <summary>Defines storage, delivery, lease, and retry limits for a work queue.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueuePolicy)]
public sealed record QueuePolicy
{
    private const int DefaultMaxAttempts = 5;
    private const int DefaultMaxStoredMessages = 100_000;
    private const int DefaultMaxStoredBytes = 1_073_741_824;
    private const int DefaultMaxInFlightMessages = 1_000;
    private const int DefaultMaxInFlightBytes = 67_108_864;
    private const int DefaultMaxLeaseSeconds = 300;
    private const int DefaultRetryBaseMilliseconds = 1_000;
    private const int DefaultRetryMaxMilliseconds = 300_000;

    /// <summary>Gets or sets the max attempts value.</summary>
    [Orleans.Id(0)]
    public int MaxAttempts { get; init; } = DefaultMaxAttempts;
    /// <summary>Gets or sets the max stored messages value.</summary>
    [Orleans.Id(1)]
    public long MaxStoredMessages { get; init; } = DefaultMaxStoredMessages;
    /// <summary>Gets or sets the max stored bytes value.</summary>
    [Orleans.Id(2)]
    public long MaxStoredBytes { get; init; } = DefaultMaxStoredBytes;
    /// <summary>Gets or sets the max in flight messages value.</summary>
    [Orleans.Id(3)]
    public int MaxInFlightMessages { get; init; } = DefaultMaxInFlightMessages;
    /// <summary>Gets or sets the max in flight bytes value.</summary>
    [Orleans.Id(4)]
    public long MaxInFlightBytes { get; init; } = DefaultMaxInFlightBytes;
    /// <summary>Gets or sets the max lease seconds value.</summary>
    [Orleans.Id(5)]
    public int MaxLeaseSeconds { get; init; } = DefaultMaxLeaseSeconds;
    /// <summary>Gets or sets the retry base milliseconds value.</summary>
    [Orleans.Id(6)]
    public int RetryBaseMilliseconds { get; init; } = DefaultRetryBaseMilliseconds;
    /// <summary>Gets or sets the retry max milliseconds value.</summary>
    [Orleans.Id(7)]
    public int RetryMaxMilliseconds { get; init; } = DefaultRetryMaxMilliseconds;
}

/// <summary>Adds a message to a work queue with scheduling and ordering metadata.</summary>
/// <param name="Queue">Identifies the work queue.</param>
/// <param name="MessageId">Identifies the queue message.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
/// <param name="NotBefore">Sets the optional earliest delivery time.</param>
/// <param name="ExpiresAt">Sets the optional expiration time.</param>
/// <param name="OrderingKey">Groups messages that must retain their ordering.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EnqueueMessage)]
public sealed record EnqueueMessage([property: Orleans.Id(0)] string Queue, [property: Orleans.Id(1)] string MessageId, [property: Orleans.Id(2)] string PayloadJson, [property: Orleans.Id(3)] string HeadersJson = EnqueueMessage.DefaultHeadersJson,
    [property: Orleans.Id(4)] DateTimeOffset? NotBefore = null, [property: Orleans.Id(5)] DateTimeOffset? ExpiresAt = null, [property: Orleans.Id(6)] string? OrderingKey = null) : Mutation(Queue)
{
    private const string DefaultHeadersJson = "{}";
}

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.MessageBody)]
public sealed record MessageBody([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] string PayloadJson, [property: Orleans.Id(2)] string HeadersJson, [property: Orleans.Id(3)] string? OrderingKey, [property: Orleans.Id(4)] string Fingerprint);

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.MessageMetadata)]
public sealed record MessageMetadata([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] MessageState State, [property: Orleans.Id(2)] int Attempts, [property: Orleans.Id(3)] long StateVersion, [property: Orleans.Id(4)] long ReadySequence,
    [property: Orleans.Id(5)] DateTimeOffset? NotBefore, [property: Orleans.Id(6)] DateTimeOffset? ExpiresAt, [property: Orleans.Id(7)] string? LeaseOwner = null, [property: Orleans.Id(8)] long LeaseVersion = MessageMetadata.DefaultLeaseVersion,
    [property: Orleans.Id(9)] DateTimeOffset? LeaseUntil = null, [property: Orleans.Id(10)] long DeliveryGeneration = MessageMetadata.DefaultDeliveryGeneration, [property: Orleans.Id(11)] string? SafeFailureCode = null)
{
    private const int DefaultLeaseVersion = 0;
    private const int DefaultDeliveryGeneration = 1;
}

/// <summary>Reports queue storage and in-flight usage and the next ready sequence.</summary>
/// <param name="StoredMessages">Specifies the stored messages value.</param>
/// <param name="StoredBytes">Specifies the stored bytes value.</param>
/// <param name="InFlightMessages">Specifies the in flight messages value.</param>
/// <param name="InFlightBytes">Specifies the in flight bytes value.</param>
/// <param name="NextReadySequence">Specifies the next ready sequence value.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueueCounters)]
public sealed record QueueCounters([property: Orleans.Id(0)] long StoredMessages, [property: Orleans.Id(1)] long StoredBytes, [property: Orleans.Id(2)] long InFlightMessages, [property: Orleans.Id(3)] long InFlightBytes, [property: Orleans.Id(4)] long NextReadySequence);

/// <summary>Returns a leased message and the token and generation needed to process it.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
/// <param name="Token">Carries the delivery token.</param>
/// <param name="LeaseVersion">Identifies the lease version.</param>
/// <param name="LeaseUntil">Specifies the lease until value.</param>
/// <param name="Attempt">Specifies the attempt value.</param>
/// <param name="DeliveryGeneration">Identifies the delivery generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.Delivery)]
public sealed record Delivery([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] string PayloadJson, [property: Orleans.Id(2)] string HeadersJson, [property: Orleans.Id(3)] string Token, [property: Orleans.Id(4)] long LeaseVersion,
    [property: Orleans.Id(5)] DateTimeOffset LeaseUntil, [property: Orleans.Id(6)] int Attempt, [property: Orleans.Id(7)] long DeliveryGeneration);

/// <summary>Requests a bounded number of messages from one queue lane.</summary>
/// <param name="RequestId">Identifies this receive request.</param>
/// <param name="Lane">Identifies the queue lane.</param>
/// <param name="MaxMessages">Limits deliveries returned by the request.</param>
/// <param name="MaxBytes">Limits returned payload bytes.</param>
/// <param name="LeaseSeconds">Requests the message lease duration.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReceiveRequest)]
public sealed record ReceiveRequest([property: Orleans.Id(0)] Guid RequestId, [property: Orleans.Id(1)] QueueLaneRef Lane, [property: Orleans.Id(2)] int MaxMessages = ReceiveRequest.DefaultMaxMessages,
    [property: Orleans.Id(3)] int MaxBytes = ReceiveRequest.DefaultMaxBytes, [property: Orleans.Id(4)] int LeaseSeconds = ReceiveRequest.DefaultLeaseSeconds)
{
    private const int DefaultMaxMessages = 1;
    private const int DefaultMaxBytes = 1_048_576;
    private const int DefaultLeaseSeconds = 30;
}

/// <summary>Returns leased deliveries and their committed receive token.</summary>
/// <param name="RequestId">Identifies this receive request.</param>
/// <param name="Deliveries">Contains leased messages.</param>
/// <param name="Token">Carries the delivery token.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReceiveResult)]
public sealed record ReceiveResult([property: Orleans.Id(0)] Guid RequestId, [property: Orleans.Id(1)] ImmutableArray<Delivery> Deliveries, [property: Orleans.Id(2)] CommitToken Token);

/// <summary>Binds a delivery token to its lane, principal, lease, and incarnation.</summary>
/// <param name="Lane">Identifies the queue lane.</param>
/// <param name="MessageId">Identifies the queue message.</param>
/// <param name="PrincipalId">Identifies the principal receiving the delivery.</param>
/// <param name="LeaseVersion">Identifies the lease version.</param>
/// <param name="DeliveryGeneration">Identifies the delivery generation.</param>
/// <param name="Incarnation">Identifies the issuing storage incarnation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DeliveryClaims)]
public sealed record DeliveryClaims([property: Orleans.Id(0)] QueueLaneRef Lane, [property: Orleans.Id(1)] string MessageId, [property: Orleans.Id(2)] string PrincipalId, [property: Orleans.Id(3)] long LeaseVersion,
    [property: Orleans.Id(4)] long DeliveryGeneration, [property: Orleans.Id(5)] Guid Incarnation);

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DeliveryCommand)]
public sealed record DeliveryCommand([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] QueueLaneRef Lane, [property: Orleans.Id(2)] string Token, [property: Orleans.Id(3)] DeliveryAction Action,
    [property: Orleans.Id(4)] int? LeaseSeconds = null, [property: Orleans.Id(5)] string? FailureCode = null);

/// <summary>Requests an idempotent handler execution with its declared effects.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Lane">Identifies the queue lane.</param>
/// <param name="Token">Carries the delivery token.</param>
/// <param name="HandlerScope">Identifies the idempotent handler scope.</param>
/// <param name="ExecutionGeneration">Identifies the handler execution generation.</param>
/// <param name="Effects">Lists mutations committed with handler processing.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ProcessingRequest)]
public sealed record ProcessingRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] QueueLaneRef Lane, [property: Orleans.Id(2)] string Token, [property: Orleans.Id(3)] string HandlerScope,
    [property: Orleans.Id(4)] long ExecutionGeneration, [property: Orleans.Id(5)] ImmutableArray<Mutation> Effects);

/// <summary>Returns message metadata with any requested payload and headers.</summary>
/// <param name="Metadata">Contains the message state metadata.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.MessageInspection)]
public sealed record MessageInspection([property: Orleans.Id(0)] MessageMetadata Metadata, [property: Orleans.Id(1)] string? PayloadJson, [property: Orleans.Id(2)] string? HeadersJson);
