using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal sealed record QueueReferenceSelectState(PartitionRef Partition, McpPersistedIdentity Identity,
    CommandRequest Seed, CommitReceipt Receipt, EntityRefLifetime TargetLifetime,
    CommandRequest? Repair = null, CommitReceipt? RepairReceipt = null)
{
    internal EntityRef StoredTarget(string id) => new(Partition, QueueReferenceSelectProtocol.Target, id) { Lifetime = TargetLifetime };
}
