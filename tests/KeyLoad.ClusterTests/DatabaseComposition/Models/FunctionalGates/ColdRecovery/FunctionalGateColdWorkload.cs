using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGateColdWorkload(McpPersistedIdentity Identity, CommandRequest Command,
    CommitReceipt Receipt, DocumentReadResult Expected);
