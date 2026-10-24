using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record CapabilityOutputClusterState(McpPersistedIdentity Reader,
    (CommandRequest Command, CommitReceipt Receipt)[] Seeds, QueryCapabilityManifest Manifest);
