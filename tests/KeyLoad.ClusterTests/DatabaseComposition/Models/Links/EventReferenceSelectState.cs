using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record EventReferenceSelectState(PartitionRef Partition, McpPersistedIdentity Identity,
    CommandRequest Seed, CommitReceipt Receipt, StreamPage OriginalStream, StreamPage OriginalInvalid, EntityRefLifetime TargetLifetime,
    CommandRequest? Repair = null, CommitReceipt? RepairReceipt = null)
{
    internal EntityRef StoredTarget(string id) => new(Partition, EventReferenceSelectProtocol.Target, id) { Lifetime = TargetLifetime };
}
