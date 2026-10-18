using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record PolicyEpochPublicState(ResourcePolicyUpdateClusterScenario Scenario,
    CommandRequest Seed, CommitReceipt Receipt, string[] OriginalCursors, McpPersistedIdentity CurrentReader);
