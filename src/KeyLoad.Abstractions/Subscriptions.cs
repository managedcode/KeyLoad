using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Identifies the kind of event source.</summary>
public enum EventSourceKind
{
    /// <summary>A named business topic.</summary>
    Topic,
    /// <summary>An entity event stream.</summary>
    Stream
}

/// <summary>Identifies a topic or stream within a partition.</summary>
/// <param name="Partition">The partition containing the source.</param>
/// <param name="Resource">The topic or stream resource name.</param>
/// <param name="Kind">The type of event source.</param>
/// <param name="StreamId">The stream identifier when the source is a stream.</param>
/// <param name="Generation">The source generation used to detect replacement.</param>
public sealed record EventSourceRef(PartitionRef Partition, string Resource, EventSourceKind Kind, string? StreamId = null, long Generation = 1);

/// <summary>Reports the retained range and current tail of an event source.</summary>
/// <param name="TailPosition">The latest position assigned by the source.</param>
/// <param name="FirstAvailablePosition">The earliest position still available for reading.</param>
/// <param name="Generation">The current source generation.</param>
public sealed record EventSourceHead(long TailPosition, long FirstAvailablePosition, long Generation);

/// <summary>Represents one recorded event from a topic or stream.</summary>
/// <param name="Source">The event source that recorded the event.</param>
/// <param name="Position">The source position of the event.</param>
/// <param name="EventSequence">The sequence number within the source transaction.</param>
/// <param name="Data">The event payload and metadata.</param>
/// <param name="RecordedAt">The time at which the event was recorded.</param>
public sealed record SourceEventRecord(EventSourceRef Source, long Position, long EventSequence, EventData Data, DateTimeOffset RecordedAt);

/// <summary>Describes a bounded read from an event source.</summary>
/// <param name="Source">The source to read.</param>
/// <param name="AfterPosition">The exclusive source position from which to continue.</param>
/// <param name="Limit">The maximum number of events to return.</param>
/// <param name="Cursor">The continuation cursor for a prior page, if any.</param>
public sealed record ReadEventSourceRequest(EventSourceRef Source, long AfterPosition = 0, int Limit = 100, string? Cursor = null);

/// <summary>Contains a page of events and source continuation metadata.</summary>
/// <param name="Source">The source that was read.</param>
/// <param name="Head">The source head observed for the page.</param>
/// <param name="Events">The events in this page.</param>
/// <param name="Cursor">The continuation cursor.</param>
/// <param name="CutPosition">The committed position at which this page was read.</param>
/// <param name="HasMore">Whether additional events are available.</param>
public sealed record EventSourcePage(EventSourceRef Source, EventSourceHead Head, ImmutableArray<SourceEventRecord> Events, string Cursor,
    long CutPosition, bool HasMore);

/// <summary>Configures event retention bounds.</summary>
public sealed record EventRetentionPolicy
{
    /// <summary>Gets or sets the maximum number of retained events.</summary>
    public long MaxEvents { get; init; } = 100_000;
    /// <summary>Gets or sets the maximum number of retained event bytes.</summary>
    public long MaxBytes { get; init; } = 1_073_741_824;
}

/// <summary>Publishes a batch of events to a topic.</summary>
/// <param name="Topic">The topic name.</param>
/// <param name="Events">The events to publish.</param>
/// <param name="Generation">The topic generation expected by the operation.</param>
public sealed record PublishTopic(string Topic, ImmutableArray<EventData> Events, long Generation = 1) : Mutation(Topic);

/// <summary>Identifies one subscription group on an event source.</summary>
/// <param name="Source">The subscribed event source.</param>
/// <param name="GroupId">The subscription group identifier.</param>
public sealed record SubscriptionRef(EventSourceRef Source, string GroupId);

/// <summary>Defines the starting point used when configuring or seeking a subscription.</summary>
public enum SubscriptionStart
{
    /// <summary>Start from the earliest retained source position.</summary>
    FromBeginning,
    /// <summary>Start from the source tail at the time the operation commits.</summary>
    FromNow,
    /// <summary>Start from the position encoded by a supplied cursor.</summary>
    FromCursor
}

/// <summary>Configures retry and lease behavior for a subscription.</summary>
public sealed record SubscriptionPolicy
{
    /// <summary>Gets or sets the maximum number of outstanding deliveries.</summary>
    public int MaxWindow { get; init; } = 1_024;
    /// <summary>Gets or sets the maximum lease duration in seconds.</summary>
    public int MaxLeaseSeconds { get; init; } = 300;
    /// <summary>Gets or sets the maximum delivery attempts.</summary>
    public int MaxAttempts { get; init; } = 5;
    /// <summary>Gets or sets the base delay between retries in milliseconds.</summary>
    public int RetryBaseMilliseconds { get; init; } = 1_000;
    /// <summary>Gets or sets the maximum delay between retries in milliseconds.</summary>
    public int RetryMaxMilliseconds { get; init; } = 300_000;
}

/// <summary>Defines the principal and event-type filter for a subscription.</summary>
/// <param name="DataPrincipalId">The data principal whose scope owns the subscription.</param>
public sealed record SubscriptionDefinition(string DataPrincipalId)
{
    /// <summary>Gets or initializes the subscription retry and lease policy.</summary>
    public SubscriptionPolicy Policy { get; init; } = new();
    /// <summary>Gets or initializes the event type names accepted by the subscription.</summary>
    public ImmutableArray<string> EventTypes { get; init; } = [];
}

/// <summary>Requests creation or replacement of a subscription definition.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription to configure.</param>
/// <param name="Definition">The principal, policy, and event filter definition.</param>
/// <param name="Start">The initial source position policy.</param>
/// <param name="Cursor">The position cursor when <paramref name="Start"/> is <see cref="SubscriptionStart.FromCursor"/>.</param>
public sealed record ConfigureSubscriptionRequest(Guid CommandId, SubscriptionRef Subscription, SubscriptionDefinition Definition,
    SubscriptionStart Start = SubscriptionStart.FromBeginning, string? Cursor = null);

/// <summary>Requests moving the checkpoint of a subscription.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription to seek.</param>
/// <param name="ExpectedGeneration">The subscription generation expected by the caller.</param>
/// <param name="Start">The source position policy to apply.</param>
/// <param name="Cursor">The position cursor when <paramref name="Start"/> is <see cref="SubscriptionStart.FromCursor"/>.</param>
public sealed record SeekSubscriptionRequest(Guid CommandId, SubscriptionRef Subscription, long ExpectedGeneration,
    SubscriptionStart Start, string? Cursor = null);

/// <summary>Requests pausing or resuming a subscription.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription to update.</param>
/// <param name="ExpectedGeneration">The subscription generation expected by the caller.</param>
/// <param name="Paused">Whether to pause delivery.</param>
public sealed record SetSubscriptionPausedRequest(Guid CommandId, SubscriptionRef Subscription, long ExpectedGeneration, bool Paused);

/// <summary>Reports the current checkpoint, delivery position, and state of a subscription.</summary>
/// <param name="Subscription">The subscription identity.</param>
/// <param name="Definition">The active subscription definition.</param>
/// <param name="Generation">The subscription configuration generation.</param>
/// <param name="OwnershipEpoch">The current ownership epoch.</param>
/// <param name="Checkpoint">The highest acknowledged source position.</param>
/// <param name="IssuedPosition">The highest position issued for delivery.</param>
/// <param name="TailPosition">The latest source position observed.</param>
/// <param name="Paused">Whether delivery is paused.</param>
/// <param name="SafeFailureCode">A safe failure code when processing has failed.</param>
public sealed record SubscriptionInfo(SubscriptionRef Subscription, SubscriptionDefinition Definition, long Generation,
    long OwnershipEpoch, long Checkpoint, long IssuedPosition, long TailPosition, bool Paused, string? SafeFailureCode);

/// <summary>Requests a bounded batch of subscription deliveries.</summary>
/// <param name="RequestId">The idempotent request identifier.</param>
/// <param name="Subscription">The subscription to receive from.</param>
/// <param name="MaxEvents">The maximum number of events to return.</param>
/// <param name="MaxBytes">The maximum payload bytes to return.</param>
/// <param name="LeaseSeconds">The requested delivery lease duration in seconds.</param>
public sealed record ReceiveSubscriptionRequest(Guid RequestId, SubscriptionRef Subscription, int MaxEvents = 1,
    int MaxBytes = 1_048_576, int LeaseSeconds = 30);

/// <summary>Represents one leased event delivery.</summary>
/// <param name="Event">The delivered event.</param>
/// <param name="Token">The acknowledgement token for this delivery.</param>
/// <param name="LeaseVersion">The lease version used to reject stale acknowledgements.</param>
/// <param name="LeaseUntil">The lease expiration time.</param>
/// <param name="Attempt">The one-based delivery attempt number.</param>
public sealed record SubscriptionDelivery(SourceEventRecord Event, string Token, long LeaseVersion, DateTimeOffset LeaseUntil, int Attempt);

/// <summary>Contains deliveries and subscription status returned by a receive operation.</summary>
/// <param name="RequestId">The idempotent request identifier.</param>
/// <param name="Deliveries">The leased deliveries returned.</param>
/// <param name="Status">The subscription status observed for the request.</param>
/// <param name="Token">The commit token for the receive operation.</param>
public sealed record ReceiveSubscriptionResult(Guid RequestId, ImmutableArray<SubscriptionDelivery> Deliveries, SubscriptionInfo Status, CommitToken Token);

/// <summary>Requests an action on a leased subscription delivery.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription owning the delivery.</param>
/// <param name="Token">The delivery acknowledgement token.</param>
/// <param name="Action">The action to apply to the delivery.</param>
/// <param name="LeaseSeconds">An optional replacement lease duration in seconds.</param>
public sealed record SubscriptionDeliveryCommand(Guid CommandId, SubscriptionRef Subscription, string Token, DeliveryAction Action,
    int? LeaseSeconds = null);

/// <summary>Requests processing a delivered event and applying its resulting mutations.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription owning the delivery.</param>
/// <param name="Token">The delivery acknowledgement token.</param>
/// <param name="HandlerScope">The stable scope identifying the event handler.</param>
/// <param name="ExecutionGeneration">The handler execution generation.</param>
/// <param name="Effects">The mutations produced by processing the event.</param>
public sealed record SubscriptionProcessingRequest(Guid CommandId, SubscriptionRef Subscription, string Token, string HandlerScope,
    long ExecutionGeneration, ImmutableArray<Mutation> Effects);

/// <summary>Reports the committed result of subscription event processing.</summary>
/// <param name="Receipt">The commit receipt for the processing effects.</param>
/// <param name="AlreadyProcessed">Whether the event had already been processed.</param>
/// <param name="OriginalEffectsToken">The token of the original processing effects.</param>
public sealed record SubscriptionProcessingResult(CommitReceipt Receipt, bool AlreadyProcessed, CommitToken OriginalEffectsToken);

/// <summary>Identifies the subscription whose status is requested.</summary>
/// <param name="Subscription">The subscription to retrieve.</param>
public sealed record GetSubscriptionRequest(SubscriptionRef Subscription);
