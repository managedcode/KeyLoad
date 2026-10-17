using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record SensitiveAuthorityState(McpDocumentScenario Scenario, McpPersistedIdentity Identity,
    CommandRequest Denied, CommandRequest Healthy, CommitReceipt Receipt);
