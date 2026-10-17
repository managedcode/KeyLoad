using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record DiscoveryAuthorityState(McpDocumentScenario Scenario, McpPersistedIdentity Identity,
    Guid RepairCommandId, PrincipalRecord Repaired)
{
    internal PrincipalRecord? OriginalRepairResult
    {
        get; set;
    }
}
internal sealed record DiscoveryAuthorityImage(DocumentResult? Document, OutboxStatus Outbox);
