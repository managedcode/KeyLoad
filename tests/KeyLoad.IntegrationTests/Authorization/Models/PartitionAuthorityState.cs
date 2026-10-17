using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record PartitionAuthorityState(PartitionQueryRf3Scenario Scenario,
    McpPersistedIdentity Identity, Guid RepairCommand, PrincipalRecord RepairResult);
