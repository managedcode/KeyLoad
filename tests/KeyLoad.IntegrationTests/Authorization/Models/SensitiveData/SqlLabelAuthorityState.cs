using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.Search;

namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record SqlLabelAuthorityState(GraphSearchClusterScenario Scenario,
    McpPersistedIdentity Identity, Guid RepairCommand, PrincipalRecord RepairResult);
