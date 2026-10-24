using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal sealed record GraphLabelUseClusterState(McpPersistedIdentity Reader, CommandRequest Seed, CommitReceipt Receipt);
