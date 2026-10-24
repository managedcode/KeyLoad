using KeyLoad.IntegrationTests.DocumentStorage;

namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record AdversarialAuthoritySeed(McpDocumentScenario Scenario, CommandRequest Command,
    CommitReceipt Receipt);
