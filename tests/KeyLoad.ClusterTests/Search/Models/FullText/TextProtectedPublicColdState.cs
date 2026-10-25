using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Search;

internal sealed record TextProtectedPublicColdState(TextClusterScenario Scenario,
    McpPersistedIdentity Identity, CommandRequest Patch, CommitReceipt Patched,
    CommandRequest Delete, CommitReceipt Deleted);
