using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record AdaptiveFilteredPublicRf3Batch(CommandRequest Request, CommitReceipt Receipt);
internal sealed record AdaptiveFilteredPublicRf3State(NativeAnnMaintenanceRf3Scenario Scenario,
    AdaptiveFilteredPublicRf3Batch[] Batches, AnnMaintenanceRequest Pin, AnnMaintenanceResult Published,
    McpPersistedIdentity Reader);
internal sealed record AdaptiveFilteredPublicRf3Cohort(string Name, ImmutableArray<string>? Allowed, int[] Eligible);
