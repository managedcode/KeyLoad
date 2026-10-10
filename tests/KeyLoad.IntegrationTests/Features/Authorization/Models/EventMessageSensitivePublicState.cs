namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class EventMessageSensitivePublicState(PartitionRef partition, ResourceDefinition resource,
    PrincipalRecord worker, string key, bool subscription, bool dataAuthority, bool header)
{
    internal PartitionRef Partition { get; } = partition;
    internal ResourceDefinition Resource { get; set; } = resource;
    internal PrincipalRecord Worker { get; } = worker;
    internal string Key { get; } = key;
    internal string InspectorId => Worker.Id + "-inspect";
    internal string InspectorKey => InspectorId + ".keyload-native-sensitive-inspection-credential";
    internal MessageInspection? OriginalInspection { get; set; }
    internal EventSourcePage? OriginalPage { get; set; }
    internal bool Subscription { get; } = subscription;
    internal bool DataAuthority { get; } = dataAuthority;
    internal bool Header { get; } = header;
    internal QueueLaneRef Lane => new(Partition, Resource.Name);
    internal EventSourceRef Source => new(Partition, Resource.Name, EventSourceKind.Topic);
    internal SubscriptionRef OriginalGroup => new(Source, EventMessageSensitivePublicProtocol.Group);
    internal SubscriptionRef HealthyGroup => new(Source, EventMessageSensitivePublicProtocol.FreshGroup);
    internal Guid OriginalId { get; set; }
    internal object OriginalRequest { get; set; } = null!;
    internal object Original { get; set; } = null!;
    internal Guid HealthyId { get; set; }
    internal object HealthyRequest { get; set; } = null!;
    internal object Healthy { get; set; } = null!;
    internal NodeStatus Before { get; set; } = null!;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Commands { get; } = [];
    internal List<(object Command, CommitReceipt Receipt)> Completions { get; } = [];
}
