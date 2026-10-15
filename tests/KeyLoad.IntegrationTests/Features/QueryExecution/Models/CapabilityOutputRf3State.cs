using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record CapabilityOutputRf3State(McpPersistedIdentity Reader,
    (CommandRequest Command, CommitReceipt Receipt)[] Seeds, QueryCapabilityManifest Manifest);
