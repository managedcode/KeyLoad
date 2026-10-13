
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueTerminalPolicyRf3State(PartitionRef partition, ResourceDefinition definition,
    MessagingRf3Identity identity, AtomicPartitionPlacementResolution placement)
{
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal PartitionRef Partition { get; } = partition;
    internal ResourceDefinition Definition { get; } = definition;
    internal MessagingRf3Identity Identity { get; } = identity;
    internal QueueLaneRef Lane => new(Partition, QueueTerminalPolicyRf3Protocol.Queue);
    internal MessageInspection Original { get; set; } = new(new(QueueTerminalPolicyRf3Protocol.Message,
        MessageState.Ready, 0, 1L, 1L, null, null), QueueTerminalPolicyRf3Protocol.Payload, QueueTerminalPolicyRf3Protocol.Headers);
    internal MessageInspection? Healthy { get; set; }
    internal OutboxStatus Baseline { get; set; } = null!;
    internal List<(CommandRequest Request, CommitReceipt Result)> Producers { get; } = [];
    internal List<(ReceiveRequest Request, ReceiveResult Result)> Claims { get; } = [];
    internal List<(DeliveryCommand Request, CommitReceipt Result)> Completions { get; } = [];
    internal List<(Guid Id, ConfigureResourceRequest Request, ResourceDefinition Result)> Configurations { get; } = [];
    internal Guid RefusedPolicy { get; } = Guid.NewGuid();
    internal CommandRequest RefusedProducer { get; } = new(Guid.NewGuid(), partition,
        [new PutDocument(QueueTerminalPolicyRf3Protocol.Collection, QueueTerminalPolicyRf3Protocol.Refused,
            QueueTerminalPolicyRf3Protocol.Payload), new EnqueueMessage(QueueTerminalPolicyRf3Protocol.Queue,
            QueueTerminalPolicyRf3Protocol.Message, QueueTerminalPolicyRf3Protocol.Payload)]);
}
