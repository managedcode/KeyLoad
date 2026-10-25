using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;

namespace KeyLoad.ClusterTests.Authorization;

internal sealed record SensitiveAuthorityState(McpDocumentScenario Scenario, McpPersistedIdentity Identity,
    CommandRequest Denied, CommandRequest Healthy, CommitReceipt Receipt);
