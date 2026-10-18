
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueTerminalPolicyClusterState(PartitionRef partition, ResourceDefinition definition,
    MessagingClusterIdentity identity)
{
    internal PartitionRef Partition { get; } = partition;
    internal ResourceDefinition Definition { get; } = definition;
    internal MessagingClusterIdentity Identity { get; } = identity;
    internal QueueLaneRef Lane => new(Partition, QueueTerminalPolicyClusterProtocol.Queue);
    internal MessageInspection Original
    {
        get; set;
    } = new(new(QueueTerminalPolicyClusterProtocol.Message,
        MessageState.Ready, 0, 1L, 1L, null, null), QueueTerminalPolicyClusterProtocol.Payload, QueueTerminalPolicyClusterProtocol.Headers);
    internal MessageInspection? Healthy
    {
        get; set;
    }
    internal OutboxStatus Baseline { get; set; } = null!;
    internal List<(CommandRequest Request, CommitReceipt Result)> Producers { get; } = [];
    internal List<(ReceiveRequest Request, ReceiveResult Result)> Claims { get; } = [];
    internal List<(DeliveryCommand Request, CommitReceipt Result)> Completions { get; } = [];
    internal List<(Guid Id, ConfigureResourceRequest Request, ResourceDefinition Result)> Configurations { get; } = [];
    internal Guid RefusedPolicy { get; } = Guid.NewGuid();
    internal CommandRequest RefusedProducer
    {
        get;
    } = new(Guid.NewGuid(), partition,
        [new PutDocument(QueueTerminalPolicyClusterProtocol.Collection, QueueTerminalPolicyClusterProtocol.Refused,
            QueueTerminalPolicyClusterProtocol.Payload), new EnqueueMessage(QueueTerminalPolicyClusterProtocol.Queue,
            QueueTerminalPolicyClusterProtocol.Message, QueueTerminalPolicyClusterProtocol.Payload)]);
}
