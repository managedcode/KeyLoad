using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.DocumentStorage;

namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record DiscoveryAuthorityState(McpDocumentScenario Scenario, McpPersistedIdentity Identity,
    Guid RepairCommandId, PrincipalRecord Repaired)
{
    internal PrincipalRecord? OriginalRepairResult
    {
        get; set;
    }
}
