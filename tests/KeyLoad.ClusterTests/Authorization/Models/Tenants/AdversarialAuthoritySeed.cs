using KeyLoad.ClusterTests.DocumentStorage;

namespace KeyLoad.ClusterTests.Authorization;

internal sealed record AdversarialAuthoritySeed(McpDocumentScenario Scenario, CommandRequest Command,
    CommitReceipt Receipt);
