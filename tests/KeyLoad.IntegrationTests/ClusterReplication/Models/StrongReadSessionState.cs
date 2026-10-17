using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed record StrongReadSessionState(PartitionRef Partition, McpPersistedIdentity Identity,
    StrongReadSessionCommit[] Commits, OutboxStatus Outbox, DocumentResult? Target);
