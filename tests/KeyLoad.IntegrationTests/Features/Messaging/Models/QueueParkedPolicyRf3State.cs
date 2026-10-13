namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueParkedPolicyRf3State(PartitionRef partition, ResourceDefinition definition,
    MessagingRf3Identity identity, AtomicPartitionPlacementResolution placement)
{
    internal PartitionRef Partition { get; } = partition;
    internal ResourceDefinition Definition { get; set; } = definition;
    internal MessagingRf3Identity Identity { get; } = identity;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal QueueLaneRef Lane => new(Partition, QueueParkedPolicyRf3Protocol.Queue);
    internal Dictionary<string, MessageInspection?> Messages { get; } = new(StringComparer.Ordinal)
    {
        [QueueParkedPolicyRf3Protocol.OriginalHead] = null,
        [QueueParkedPolicyRf3Protocol.OriginalSibling] = null,
        [QueueParkedPolicyRf3Protocol.FreshHead] = null,
        [QueueParkedPolicyRf3Protocol.FreshSibling] = null
    };
    internal List<(CommandRequest Request, CommitReceipt Result)> Commands { get; } = [];
    internal List<(DeliveryCommand Request, CommitReceipt Result)> Completions { get; } = [];
    internal List<(Guid Id, ConfigureResourceRequest Request, ResourceDefinition Result)> Configurations { get; } = [];
    internal System.Text.Json.JsonElement? RefusedProblem { get; set; }
    internal System.Text.Json.JsonElement? RefusedMcpError { get; set; }
    internal Guid RefusedPolicy { get; } = Guid.NewGuid();
    internal ConfigureResourceRequest Replacement { get; } = new(partition.TenantId, partition.DatabaseId,
        definition with
        {
            SchemaVersion = 2L,
            QueuePolicy = definition.QueuePolicy with
            { OrderingProfile = QueueOrderingProfile.CompetingConsumers, ParkedHeadPolicy = QueueParkedHeadPolicy.Continue }
        })
    { ExpectedSchemaVersion = 1L };
    internal OutboxStatus Baseline { get; set; } = null!;
}
