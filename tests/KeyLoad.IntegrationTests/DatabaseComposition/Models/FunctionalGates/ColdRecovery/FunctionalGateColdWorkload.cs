using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal sealed record FunctionalGateColdWorkload(McpPersistedIdentity Identity, CommandRequest Command,
    CommitReceipt Receipt, DocumentReadResult Expected);
