using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextProtectedPublicColdState(NativeTextRf3Scenario Scenario,
    McpPersistedIdentity Identity, CommandRequest Patch, CommitReceipt Patched,
    CommandRequest Delete, CommitReceipt Deleted);
