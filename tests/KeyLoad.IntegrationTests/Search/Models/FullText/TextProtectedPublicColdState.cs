using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record TextProtectedPublicColdState(TextClusterScenario Scenario,
    McpPersistedIdentity Identity, CommandRequest Patch, CommitReceipt Patched,
    CommandRequest Delete, CommitReceipt Deleted);
