using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed record GraphLabelUseRf3State(McpPersistedIdentity Reader, CommandRequest Seed, CommitReceipt Receipt);
