using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.StorageReplication;

internal sealed record StrongReadSessionState(PartitionRef Partition, McpPersistedIdentity Identity,
    StrongReadSessionCommit[] Commits, OutboxStatus Outbox, DocumentResult? Target, EntityRefLifetime Lifetime)
{
    internal EntityRef Stored(string id) => new(Partition, StrongReadSessionProtocol.Collection, id) { Lifetime = Lifetime };
}
