using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record AdversarialAuthoritySeed(McpDocumentScenario Scenario, CommandRequest Command,
    CommitReceipt Receipt);
