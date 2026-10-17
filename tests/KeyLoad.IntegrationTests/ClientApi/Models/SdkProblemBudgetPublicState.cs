using KeyLoad.IntegrationTests.Features.DocumentStorage;
namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal sealed record SdkProblemBudgetPublicState(McpPersistedIdentity Identity,
    CommandRequest Seed, CommitReceipt SeedReceipt, CommandRequest Denied, CommandRequest Healthy,
    AtomicPartitionPlacementResolution Placement, OutboxStatus Outbox, NodeStatus Status,
    CommitReceipt? HealthyReceipt = null)
{
    internal EntityRef Allowed => new(Seed.Partition, McpDocumentProtocol.Collection, SdkProblemBudgetPublicPreparation.Entity);
    internal EntityRef Forbidden => new(Seed.Partition, SdkProblemBudgetPublicPreparation.DeniedCollection,
        SdkProblemBudgetPublicPreparation.Entity);
}
