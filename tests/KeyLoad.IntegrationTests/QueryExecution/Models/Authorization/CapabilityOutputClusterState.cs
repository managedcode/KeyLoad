using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record CapabilityOutputClusterState(McpPersistedIdentity Reader,
    (CommandRequest Command, CommitReceipt Receipt)[] Seeds, QueryCapabilityManifest Manifest);
