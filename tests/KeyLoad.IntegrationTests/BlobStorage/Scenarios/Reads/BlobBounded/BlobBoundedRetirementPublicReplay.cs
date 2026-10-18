using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobBoundedRetirementPublicReplay
{
    internal static async Task MatchingAsync<T>(RequestCqrsClusterCallers callers, string tool, object request,
        T expected, Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> invoke,
        Func<Task> observe, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await invoke(token)));
        await observe();
        await SqlClusterProtocol.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<T>(await callers.Mcp.CallAsync(tool, request, token))).Value);
        await observe();
    }

    internal static async Task PublicationAsync(RequestCqrsClusterCallers callers,
        BlobBoundedRetirementPublicPublication.Published old, Func<Task> observe, CancellationToken token)
    {
        await MatchingAsync(callers, "keyload_blobs_begin_upload", old.Begin, old.Begun,
            ct => callers.Sdk.BeginBlobUploadAsync(old.Begin, ct), observe, token);
        for (var i = 0; i < old.Parts.Length; i++)
        {
            var part = old.Parts[i];
            await MatchingAsync(callers, "keyload_blobs_write_part", part, old.Written[i],
                ct => callers.Sdk.WriteBlobPartAsync(part, ct), observe, token);
        }
        await MatchingAsync(callers, "keyload_blobs_complete_upload", old.Complete, old.Completed,
            ct => callers.Sdk.CompleteBlobUploadAsync(old.Complete, ct), observe, token);
        await Assert.That(old.Begun.Receipt.CommandId).IsEqualTo(old.Begin.CommandId);
        await Assert.That(old.Completed.Receipt.CommandId).IsEqualTo(old.Complete.CommandId);
        await Assert.That(old.Begun.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(old.Completed.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        for (var i = 0; i < old.Parts.Length; i++)
        {
            await Assert.That(old.Written[i].Receipt.CommandId).IsEqualTo(old.Parts[i].CommandId);
            await Assert.That(old.Written[i].Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        }
    }
}
