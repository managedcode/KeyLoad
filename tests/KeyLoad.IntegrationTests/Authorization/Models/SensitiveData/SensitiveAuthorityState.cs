using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.DocumentStorage;

namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record SensitiveAuthorityState(McpDocumentScenario Scenario, McpPersistedIdentity Identity,
    CommandRequest Denied, CommandRequest Healthy, CommitReceipt Receipt);
