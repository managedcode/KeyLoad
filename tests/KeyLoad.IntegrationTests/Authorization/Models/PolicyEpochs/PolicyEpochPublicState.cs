using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record PolicyEpochPublicState(ResourcePolicyUpdateClusterScenario Scenario,
    CommandRequest Seed, CommitReceipt Receipt, string[] OriginalCursors, McpPersistedIdentity CurrentReader);
