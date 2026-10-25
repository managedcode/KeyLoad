using KeyLoad.ClusterTests.DocumentStorage;
namespace KeyLoad.ClusterTests.ClientApi;

internal sealed record SdkProblemBudgetPublicState(McpPersistedIdentity Identity,
    CommandRequest Seed, CommitReceipt SeedReceipt, CommandRequest Denied, CommandRequest Healthy,
    OutboxStatus Outbox, NodeStatus Status, EntityRefLifetime AllowedLifetime, EntityRefLifetime ForbiddenLifetime,
    CommitReceipt? HealthyReceipt = null)
{
    internal EntityRef Allowed => new(Seed.Partition, McpDocumentProtocol.Collection, SdkProblemBudgetPublicPreparation.Entity);
    internal EntityRef Forbidden => new(Seed.Partition, SdkProblemBudgetPublicPreparation.DeniedCollection,
        SdkProblemBudgetPublicPreparation.Entity);
    internal EntityRef StoredAllowed => Allowed with { Lifetime = AllowedLifetime };
    internal EntityRef StoredForbidden => Forbidden with { Lifetime = ForbiddenLifetime };
}
