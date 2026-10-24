using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.StorageReplication;

internal sealed record StrongReadSessionState(PartitionRef Partition, McpPersistedIdentity Identity,
    StrongReadSessionCommit[] Commits, OutboxStatus Outbox, DocumentResult? Target);
