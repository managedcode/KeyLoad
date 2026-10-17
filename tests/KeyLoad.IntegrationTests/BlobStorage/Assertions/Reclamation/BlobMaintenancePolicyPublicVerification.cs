using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobMaintenancePolicyPublicVerification
{
    internal static async Task FinalAsync(RequestCqrsRf3Callers creator, BlobMaintenancePolicyPublicState state, CancellationToken token)
    {
        Task Observe() => BlobMaintenancePolicyPublicAssertions.FinalAsync(creator, state, token);
        await Observe();
        await BlobMaintenancePolicyPublicAssertions.DeleteDeniedAsync(creator, state.DeniedDelete, Observe, token);
        await BlobMaintenancePolicyPublicAssertions.DeleteDeniedAsync(creator, state.Delete, Observe, token);
        await BlobMaintenancePolicyPublicAssertions.ReclaimDeniedAsync(creator, state, Observe, token);
        await BlobBoundedRetirementPublicReplay.MatchingAsync(creator, "keyload_blobs_reclaim", state.Reclaim, state.Reclaimed,
            ct => creator.Sdk.ReclaimBlobAsync(state.Reclaim, ct), Observe, token);
        await BlobBoundedRetirementPublicReplay.PublicationAsync(creator, state.Healthy, Observe, token);
    }
}
