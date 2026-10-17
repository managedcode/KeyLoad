using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateColdWorkload(McpPersistedIdentity Identity, CommandRequest Command,
    CommitReceipt Receipt, DocumentReadResult Expected);
