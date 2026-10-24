using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Authorization;

internal sealed class EventMessageSensitiveReplayState(PartitionRef partition, ResourceDefinition resource,
    PrincipalRecord worker, bool subscription, bool dataAuthority, bool header)
{
    internal PartitionRef Partition { get; } = partition;
    internal ResourceDefinition Resource { get; set; } = resource;
    internal PrincipalRecord Worker { get; } = worker;
    internal bool Subscription { get; } = subscription;
    internal bool DataAuthority { get; } = dataAuthority;
    internal bool Header { get; } = header;
    internal string Caller => DataAuthority ? EventMessageSensitiveReplayProtocol.Root : Worker.Id;
    internal QueueLaneRef Lane => new(Partition, Resource.Name);
    internal EventSourceRef Source => new(Partition, Resource.Name, EventSourceKind.Topic);
    internal SubscriptionRef OriginalGroup => new(Source, EventMessageSensitiveReplayProtocol.Group);
    internal CommandRequest Producer { get; set; } = null!;
    private Result<object>? produced;
    internal Result<object> Produced
    {
        get => produced ?? throw new InvalidOperationException(MissingOutcome);
        set => produced = value;
    }
    internal Guid OriginalId
    {
        get; set;
    }
    internal object OriginalRequest { get; set; } = null!;
    private Result<object>? original;
    internal Result<object> Original
    {
        get => original ?? throw new InvalidOperationException(MissingOutcome);
        set => original = value;
    }
    internal byte[] OriginalOutcome { get; set; } = [];
    internal Guid HealthyId
    {
        get; set;
    }
    internal object HealthyRequest { get; set; } = null!;
    private Result<object>? healthy;
    internal Result<object> Healthy
    {
        get => healthy ?? throw new InvalidOperationException(MissingOutcome);
        set => healthy = value;
    }
    private const string MissingOutcome = "The original sensitive replay operation has not produced its outcome.";
    internal OperationKind ReceiveKind => Subscription ? OperationKind.ReceiveSubscription : OperationKind.Receive;
}
