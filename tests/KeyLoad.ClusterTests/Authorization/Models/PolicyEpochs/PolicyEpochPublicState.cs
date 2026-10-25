using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Authorization;

internal sealed record PolicyEpochPublicState(ResourcePolicyUpdateClusterScenario Scenario,
    CommandRequest Seed, CommitReceipt Receipt, string[] OriginalCursors, McpPersistedIdentity CurrentReader);
