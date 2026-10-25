using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;

namespace KeyLoad.ClusterTests.Authorization;

internal sealed record DiscoveryAuthorityState(McpDocumentScenario Scenario, McpPersistedIdentity Identity,
    Guid RepairCommandId, PrincipalRecord Repaired)
{
    internal PrincipalRecord? OriginalRepairResult
    {
        get; set;
    }
}
