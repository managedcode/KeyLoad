using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record AdaptiveFilteredPublicRf3State(NativeAnnMaintenanceRf3Scenario Scenario,
    AdaptiveFilteredPublicRf3Batch[] Batches, AnnMaintenanceRequest Pin, AnnMaintenanceResult Published,
    McpPersistedIdentity Reader);
