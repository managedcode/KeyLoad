using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record PartitionAuthorityState(PartitionQueryClusterScenario Scenario,
    McpPersistedIdentity Identity, Guid RepairCommand, PrincipalRecord RepairResult);
