namespace KeyLoad;

public enum EventSourceKind { Topic, Stream }
public sealed record EventSourceRef(PartitionRef Partition, string Resource, EventSourceKind Kind, string? StreamId = null, long Generation = 1);
public sealed record EventSourceHead(long TailPosition, long FirstAvailablePosition, long Generation);
public sealed record SourceEventRecord(EventSourceRef Source, long Position, long EventSequence, EventData Data, DateTimeOffset RecordedAt);
public sealed record ReadEventSourceRequest(EventSourceRef Source, long AfterPosition = 0, int Limit = 100, string? Cursor = null);
public sealed record EventSourcePage(EventSourceRef Source, EventSourceHead Head, SourceEventRecord[] Events, string Cursor,
    long CutPosition, bool HasMore);
public sealed record EventRetentionPolicy
{
    public long MaxEvents { get; init; } = 100_000;
    public long MaxBytes { get; init; } = 1_073_741_824;
}
public sealed record PublishTopic(string Topic, EventData[] Events, long Generation = 1) : Mutation(Topic);
public sealed record SubscriptionRef(EventSourceRef Source, string GroupId);
public enum SubscriptionStart { FromBeginning, FromNow, FromCursor }
public sealed record SubscriptionPolicy
{
    public int MaxWindow { get; init; } = 1_024;
    public int MaxLeaseSeconds { get; init; } = 300;
    public int MaxAttempts { get; init; } = 5;
    public int RetryBaseMilliseconds { get; init; } = 1_000;
    public int RetryMaxMilliseconds { get; init; } = 300_000;
}
public sealed record SubscriptionDefinition(string DataPrincipalId)
{
    public SubscriptionPolicy Policy { get; init; } = new();
    public string[] EventTypes { get; init; } = [];
}
public sealed record ConfigureSubscriptionRequest(Guid CommandId, SubscriptionRef Subscription, SubscriptionDefinition Definition,
    SubscriptionStart Start = SubscriptionStart.FromBeginning, string? Cursor = null);
public sealed record SeekSubscriptionRequest(Guid CommandId, SubscriptionRef Subscription, long ExpectedGeneration,
    SubscriptionStart Start, string? Cursor = null);
public sealed record SetSubscriptionPausedRequest(Guid CommandId, SubscriptionRef Subscription, long ExpectedGeneration, bool Paused);
public sealed record SubscriptionInfo(SubscriptionRef Subscription, SubscriptionDefinition Definition, long Generation,
    long OwnershipEpoch, long Checkpoint, long IssuedPosition, long TailPosition, bool Paused, string? SafeFailureCode);
public sealed record ReceiveSubscriptionRequest(Guid RequestId, SubscriptionRef Subscription, int MaxEvents = 1,
    int MaxBytes = 1_048_576, int LeaseSeconds = 30);
public sealed record SubscriptionDelivery(SourceEventRecord Event, string Token, long LeaseVersion, DateTimeOffset LeaseUntil, int Attempt);
public sealed record ReceiveSubscriptionResult(Guid RequestId, SubscriptionDelivery[] Deliveries, SubscriptionInfo Status, CommitToken Token);
public sealed record SubscriptionDeliveryCommand(Guid CommandId, SubscriptionRef Subscription, string Token, DeliveryAction Action,
    int? LeaseSeconds = null);
public sealed record SubscriptionProcessingRequest(Guid CommandId, SubscriptionRef Subscription, string Token, string HandlerScope,
    long ExecutionGeneration, Mutation[] Effects);
public sealed record SubscriptionProcessingResult(CommitReceipt Receipt, bool AlreadyProcessed, CommitToken OriginalEffectsToken);
public sealed record GetSubscriptionRequest(SubscriptionRef Subscription);
