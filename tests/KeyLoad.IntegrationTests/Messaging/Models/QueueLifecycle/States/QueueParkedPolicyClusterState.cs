namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueParkedPolicyClusterState(PartitionRef partition, ResourceDefinition definition,
    MessagingClusterIdentity identity)
{
    internal PartitionRef Partition { get; } = partition;
    internal ResourceDefinition Definition { get; set; } = definition;
    internal MessagingClusterIdentity Identity { get; } = identity;
    internal QueueLaneRef Lane => new(Partition, QueueParkedPolicyClusterProtocol.Queue);
    internal Dictionary<string, MessageInspection?> Messages
    {
        get;
    } = new(StringComparer.Ordinal)
    {
        [QueueParkedPolicyClusterProtocol.OriginalHead] = null,
        [QueueParkedPolicyClusterProtocol.OriginalSibling] = null,
        [QueueParkedPolicyClusterProtocol.FreshHead] = null,
        [QueueParkedPolicyClusterProtocol.FreshSibling] = null
    };
    internal List<(CommandRequest Request, CommitReceipt Result)> Commands { get; } = [];
    internal List<(DeliveryCommand Request, CommitReceipt Result)> Completions { get; } = [];
    internal List<(Guid Id, ConfigureResourceRequest Request, ResourceDefinition Result)> Configurations { get; } = [];
    internal System.Text.Json.JsonElement? RefusedProblem
    {
        get; set;
    }
    internal System.Text.Json.JsonElement? RefusedMcpError
    {
        get; set;
    }
    internal Guid RefusedPolicy { get; } = Guid.NewGuid();
    internal ConfigureResourceRequest Replacement
    {
        get;
    } = new(partition.TenantId, partition.DatabaseId,
        definition with
        {
            SchemaVersion = 2L,
            QueuePolicy = definition.QueuePolicy with
            {
                OrderingProfile = QueueOrderingProfile.CompetingConsumers,
                ParkedHeadPolicy = QueueParkedHeadPolicy.Continue
            }
        })
    {
        ExpectedSchemaVersion = 1L
    };
    internal OutboxStatus Baseline { get; set; } = null!;
}
