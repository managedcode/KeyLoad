namespace KeyLoad.UnitTests.Features.Authorization;

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
    internal OperationResult Produced { get; set; } = null!;
    internal Guid OriginalId { get; set; }
    internal object OriginalRequest { get; set; } = null!;
    internal OperationResult Original { get; set; } = null!;
    internal byte[] OriginalOutcome { get; set; } = [];
    internal Guid HealthyId { get; set; }
    internal object HealthyRequest { get; set; } = null!;
    internal OperationResult Healthy { get; set; } = null!;
    internal OperationKind ReceiveKind => Subscription ? OperationKind.ReceiveSubscription : OperationKind.Receive;
}
