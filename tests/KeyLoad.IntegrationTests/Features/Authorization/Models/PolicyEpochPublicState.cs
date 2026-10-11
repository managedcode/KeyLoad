using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record PolicyEpochPublicState(ResourcePolicyUpdateRf3Scenario Scenario,
    CommandRequest Seed, CommitReceipt Receipt, string[] OriginalCursors, McpPersistedIdentity CurrentReader);
