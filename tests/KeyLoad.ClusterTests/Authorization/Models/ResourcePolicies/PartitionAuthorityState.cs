using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal sealed record PartitionAuthorityState(PartitionQueryClusterScenario Scenario,
    McpPersistedIdentity Identity, Guid RepairCommand, PrincipalRecord RepairResult);
