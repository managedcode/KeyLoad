using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobLiveListR43Replay
{
    internal static async Task RunAsync(RequestCqrsClusterCallers callers, BlobLiveListR43State state, CancellationToken token)
    {
        for (var index = 0; index < state.Versions.Length; index++)
        {
            var target = index;
            Task Observe() => BlobLiveListR43Pages.CurrentAsync(callers, state, target, token);
            await BlobBoundedRetirementPublicReplay.PublicationAsync(callers, state.Versions[index], Observe, token);
        }
        Task Deleted() => BlobLiveListR43Pages.CurrentAsync(callers, state, 1, token);
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_delete", state.Delete, state.Deleted,
            ct => callers.Sdk.DeleteBlobAsync(state.Delete, ct), Deleted, token);
    }
}
