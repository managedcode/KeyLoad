using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.Search;

namespace KeyLoad.ClusterTests.Authorization;

internal sealed record SqlLabelAuthorityState(GraphSearchClusterScenario Scenario,
    McpPersistedIdentity Identity, Guid RepairCommand, PrincipalRecord RepairResult);
