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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EventSourceRef)]
public sealed record EventSourceRef([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Resource, [property: Orleans.Id(2)] EventSourceKind Kind, [property: Orleans.Id(3)] string? StreamId = null, [property: Orleans.Id(4)] long Generation = EventSourceRef.DefaultGeneration)
{
    private const int DefaultGeneration = 1;
}

/// <summary>Reports the retained range and current tail of an event source.</summary>
/// <param name="TailPosition">The latest position assigned by the source.</param>
/// <param name="FirstAvailablePosition">The earliest position still available for reading.</param>
/// <param name="Generation">The current source generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EventSourceHead)]
public sealed record EventSourceHead([property: Orleans.Id(0)] long TailPosition, [property: Orleans.Id(1)] long FirstAvailablePosition, [property: Orleans.Id(2)] long Generation);

/// <summary>Represents one recorded event from a topic or stream.</summary>
/// <param name="Source">The event source that recorded the event.</param>
/// <param name="Position">The source position of the event.</param>
/// <param name="EventSequence">The sequence number within the source transaction.</param>
/// <param name="Data">The event payload and metadata.</param>
/// <param name="RecordedAt">The time at which the event was recorded.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SourceEventRecord)]
public sealed record SourceEventRecord([property: Orleans.Id(0)] EventSourceRef Source, [property: Orleans.Id(1)] long Position, [property: Orleans.Id(2)] long EventSequence, [property: Orleans.Id(3)] EventData Data, [property: Orleans.Id(4)] DateTimeOffset RecordedAt);

/// <summary>Describes a bounded read from an event source.</summary>
/// <param name="Source">The source to read.</param>
/// <param name="AfterPosition">The exclusive source position from which to continue.</param>
/// <param name="Limit">The maximum number of events to return.</param>
/// <param name="Cursor">The continuation cursor for a prior page, if any.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadEventSourceRequest)]
public sealed record ReadEventSourceRequest([property: Orleans.Id(0)] EventSourceRef Source, [property: Orleans.Id(1)] long AfterPosition = ReadEventSourceRequest.DefaultAfterPosition, [property: Orleans.Id(2)] int Limit = ReadEventSourceRequest.DefaultLimit, [property: Orleans.Id(3)] string? Cursor = null)
{
    private const int DefaultAfterPosition = 0;
    private const int DefaultLimit = 100;
}

/// <summary>Contains a page of events and source continuation metadata.</summary>
/// <param name="Source">The source that was read.</param>
/// <param name="Head">The source head observed for the page.</param>
/// <param name="Events">The events in this page.</param>
/// <param name="Cursor">The continuation cursor.</param>
/// <param name="CutPosition">The committed position at which this page was read.</param>
/// <param name="HasMore">Whether additional events are available.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EventSourcePage)]
public sealed record EventSourcePage([property: Orleans.Id(0)] EventSourceRef Source, [property: Orleans.Id(1)] EventSourceHead Head, [property: Orleans.Id(2)] ImmutableArray<SourceEventRecord> Events, [property: Orleans.Id(3)] string Cursor,
    [property: Orleans.Id(4)] long CutPosition, [property: Orleans.Id(5)] bool HasMore);

/// <summary>Configures event retention bounds.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EventRetentionPolicy)]
public sealed record EventRetentionPolicy
{
    private const int DefaultMaxEvents = 100_000;
    private const int DefaultMaxBytes = 1_073_741_824;

    /// <summary>Gets or sets the maximum number of retained events.</summary>
    [Orleans.Id(0)]
    public long MaxEvents { get; init; } = DefaultMaxEvents;
    /// <summary>Gets or sets the maximum number of retained event bytes.</summary>
    [Orleans.Id(1)]
    public long MaxBytes { get; init; } = DefaultMaxBytes;
}

/// <summary>Publishes a batch of events to a topic.</summary>
/// <param name="Topic">The topic name.</param>
/// <param name="Events">The events to publish.</param>
/// <param name="Generation">The topic generation expected by the operation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.PublishTopic)]
public sealed record PublishTopic([property: Orleans.Id(0)] string Topic, [property: Orleans.Id(1)] ImmutableArray<EventData> Events, [property: Orleans.Id(2)] long Generation = PublishTopic.DefaultGeneration) : Mutation(Topic)
{
    private const int DefaultGeneration = 1;
}

/// <summary>Identifies one subscription group on an event source.</summary>
/// <param name="Source">The subscribed event source.</param>
/// <param name="GroupId">The subscription group identifier.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SubscriptionRef)]
public sealed record SubscriptionRef([property: Orleans.Id(0)] EventSourceRef Source, [property: Orleans.Id(1)] string GroupId);

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SubscriptionPolicy)]
public sealed record SubscriptionPolicy
{
    private const int DefaultMaxWindow = 1_024;
    private const int DefaultMaxLeaseSeconds = 300;
    private const int DefaultMaxAttempts = 5;
    private const int DefaultRetryBaseMilliseconds = 1_000;
    private const int DefaultRetryMaxMilliseconds = 300_000;

    /// <summary>Gets or sets the maximum number of outstanding deliveries.</summary>
    [Orleans.Id(0)]
    public int MaxWindow { get; init; } = DefaultMaxWindow;
    /// <summary>Gets or sets the maximum lease duration in seconds.</summary>
    [Orleans.Id(1)]
    public int MaxLeaseSeconds { get; init; } = DefaultMaxLeaseSeconds;
    /// <summary>Gets or sets the maximum delivery attempts.</summary>
    [Orleans.Id(2)]
    public int MaxAttempts { get; init; } = DefaultMaxAttempts;
    /// <summary>Gets or sets the base delay between retries in milliseconds.</summary>
    [Orleans.Id(3)]
    public int RetryBaseMilliseconds { get; init; } = DefaultRetryBaseMilliseconds;
    /// <summary>Gets or sets the maximum delay between retries in milliseconds.</summary>
    [Orleans.Id(4)]
    public int RetryMaxMilliseconds { get; init; } = DefaultRetryMaxMilliseconds;
}

/// <summary>Defines the principal and event-type filter for a subscription.</summary>
/// <param name="DataPrincipalId">The data principal whose scope owns the subscription.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SubscriptionDefinition)]
public sealed record SubscriptionDefinition([property: Orleans.Id(0)] string DataPrincipalId)
{
    /// <summary>Gets or initializes the subscription retry and lease policy.</summary>
    [Orleans.Id(1)]
    public SubscriptionPolicy Policy { get; init; } = new();
    /// <summary>Gets or initializes the event type names accepted by the subscription.</summary>
    [Orleans.Id(2)]
    public ImmutableArray<string> EventTypes { get; init; } = [];
}

/// <summary>Requests creation or replacement of a subscription definition.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription to configure.</param>
/// <param name="Definition">The principal, policy, and event filter definition.</param>
/// <param name="Start">The initial source position policy.</param>
/// <param name="Cursor">The position cursor when <paramref name="Start"/> is <see cref="SubscriptionStart.FromCursor"/>.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ConfigureSubscriptionRequest)]
public sealed record ConfigureSubscriptionRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] SubscriptionDefinition Definition,
    [property: Orleans.Id(3)] SubscriptionStart Start = SubscriptionStart.FromBeginning, [property: Orleans.Id(4)] string? Cursor = null);

/// <summary>Requests moving the checkpoint of a subscription.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription to seek.</param>
/// <param name="ExpectedGeneration">The subscription generation expected by the caller.</param>
/// <param name="Start">The source position policy to apply.</param>
/// <param name="Cursor">The position cursor when <paramref name="Start"/> is <see cref="SubscriptionStart.FromCursor"/>.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SeekSubscriptionRequest)]
public sealed record SeekSubscriptionRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] long ExpectedGeneration,
    [property: Orleans.Id(3)] SubscriptionStart Start, [property: Orleans.Id(4)] string? Cursor = null);

/// <summary>Requests pausing or resuming a subscription.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription to update.</param>
/// <param name="ExpectedGeneration">The subscription generation expected by the caller.</param>
/// <param name="Paused">Whether to pause delivery.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SetSubscriptionPausedRequest)]
public sealed record SetSubscriptionPausedRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] long ExpectedGeneration, [property: Orleans.Id(3)] bool Paused);

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SubscriptionInfo)]
public sealed record SubscriptionInfo([property: Orleans.Id(0)] SubscriptionRef Subscription, [property: Orleans.Id(1)] SubscriptionDefinition Definition, [property: Orleans.Id(2)] long Generation,
    [property: Orleans.Id(3)] long OwnershipEpoch, [property: Orleans.Id(4)] long Checkpoint, [property: Orleans.Id(5)] long IssuedPosition, [property: Orleans.Id(6)] long TailPosition, [property: Orleans.Id(7)] bool Paused, [property: Orleans.Id(8)] string? SafeFailureCode);

/// <summary>Requests a bounded batch of subscription deliveries.</summary>
/// <param name="RequestId">The idempotent request identifier.</param>
/// <param name="Subscription">The subscription to receive from.</param>
/// <param name="MaxEvents">The maximum number of events to return.</param>
/// <param name="MaxBytes">The maximum payload bytes to return.</param>
/// <param name="LeaseSeconds">The requested delivery lease duration in seconds.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReceiveSubscriptionRequest)]
public sealed record ReceiveSubscriptionRequest([property: Orleans.Id(0)] Guid RequestId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] int MaxEvents = ReceiveSubscriptionRequest.DefaultMaxEvents,
    [property: Orleans.Id(3)] int MaxBytes = ReceiveSubscriptionRequest.DefaultMaxBytes, [property: Orleans.Id(4)] int LeaseSeconds = ReceiveSubscriptionRequest.DefaultLeaseSeconds)
{
    private const int DefaultMaxEvents = 1;
    private const int DefaultMaxBytes = 1_048_576;
    private const int DefaultLeaseSeconds = 30;
}

/// <summary>Represents one leased event delivery.</summary>
/// <param name="Event">The delivered event.</param>
/// <param name="Token">The acknowledgement token for this delivery.</param>
/// <param name="LeaseVersion">The lease version used to reject stale acknowledgements.</param>
/// <param name="LeaseUntil">The lease expiration time.</param>
/// <param name="Attempt">The one-based delivery attempt number.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SubscriptionDelivery)]
public sealed record SubscriptionDelivery([property: Orleans.Id(0)] SourceEventRecord Event, [property: Orleans.Id(1)] string Token, [property: Orleans.Id(2)] long LeaseVersion, [property: Orleans.Id(3)] DateTimeOffset LeaseUntil, [property: Orleans.Id(4)] int Attempt);

/// <summary>Contains deliveries and subscription status returned by a receive operation.</summary>
/// <param name="RequestId">The idempotent request identifier.</param>
/// <param name="Deliveries">The leased deliveries returned.</param>
/// <param name="Status">The subscription status observed for the request.</param>
/// <param name="Token">The commit token for the receive operation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReceiveSubscriptionResult)]
public sealed record ReceiveSubscriptionResult([property: Orleans.Id(0)] Guid RequestId, [property: Orleans.Id(1)] ImmutableArray<SubscriptionDelivery> Deliveries, [property: Orleans.Id(2)] SubscriptionInfo Status, [property: Orleans.Id(3)] CommitToken Token);

/// <summary>Requests an action on a leased subscription delivery.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription owning the delivery.</param>
/// <param name="Token">The delivery acknowledgement token.</param>
/// <param name="Action">The action to apply to the delivery.</param>
/// <param name="LeaseSeconds">An optional replacement lease duration in seconds.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SubscriptionDeliveryCommand)]
public sealed record SubscriptionDeliveryCommand([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] string Token, [property: Orleans.Id(3)] DeliveryAction Action,
    [property: Orleans.Id(4)] int? LeaseSeconds = null);

/// <summary>Requests processing a delivered event and applying its resulting mutations.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription owning the delivery.</param>
/// <param name="Token">The delivery acknowledgement token.</param>
/// <param name="HandlerScope">The stable scope identifying the event handler.</param>
/// <param name="ExecutionGeneration">The handler execution generation.</param>
/// <param name="Effects">The mutations produced by processing the event.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SubscriptionProcessingRequest)]
public sealed record SubscriptionProcessingRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] string Token, [property: Orleans.Id(3)] string HandlerScope,
    [property: Orleans.Id(4)] long ExecutionGeneration, [property: Orleans.Id(5)] ImmutableArray<Mutation> Effects);

/// <summary>Reports the committed result of subscription event processing.</summary>
/// <param name="Receipt">The commit receipt for the processing effects.</param>
/// <param name="AlreadyProcessed">Whether the event had already been processed.</param>
/// <param name="OriginalEffectsToken">The token of the original processing effects.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SubscriptionProcessingResult)]
public sealed record SubscriptionProcessingResult([property: Orleans.Id(0)] CommitReceipt Receipt, [property: Orleans.Id(1)] bool AlreadyProcessed, [property: Orleans.Id(2)] CommitToken OriginalEffectsToken);

/// <summary>Identifies the subscription whose status is requested.</summary>
/// <param name="Subscription">The subscription to retrieve.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.GetSubscriptionRequest)]
public sealed record GetSubscriptionRequest([property: Orleans.Id(0)] SubscriptionRef Subscription);
