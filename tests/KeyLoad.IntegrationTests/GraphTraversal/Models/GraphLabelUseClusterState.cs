using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed record GraphLabelUseClusterState(McpPersistedIdentity Reader, CommandRequest Seed, CommitReceipt Receipt);
