namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueLifecyclePublicState(PartitionRef Partition, string OperatorKey, string DeniedKey, int Route)
{
    internal QueueLaneRef Lane => new(Partition, QueueLifecyclePublicProtocol.Queue);
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Commands { get; } = [];
    internal List<(DeliveryCommand Command, CommitReceipt Receipt)> Deliveries { get; } = [];
    internal Delivery? OriginalPending { get; set; }
    internal CommandRequest? FullRefusal { get; set; }
    internal CommandRequest? MixedRefusal { get; set; }
}
