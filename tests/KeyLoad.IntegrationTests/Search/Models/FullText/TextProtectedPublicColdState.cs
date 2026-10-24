using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Search;

internal sealed record TextProtectedPublicColdState(TextClusterScenario Scenario,
    McpPersistedIdentity Identity, CommandRequest Patch, CommitReceipt Patched,
    CommandRequest Delete, CommitReceipt Deleted);
