using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobEmptySlotPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
        {
            var partition = new PartitionRef("blob-empty-" + Guid.NewGuid().ToString("N"), "public-empty", "domain", Guid.NewGuid().ToString("N"));
            var definition = new ResourceDefinition("bounded-empty", ResourceKind.BlobStore, partition.TransactionDomainId)
            {
                BlobPolicy = new() { MaxBlobBytes = 1L, MaxReservedBytes = 1L, MaxObjectKeys = 1, MaxVersions = 1, MaxUploads = 1 }
            };
            await SqlClusterProtocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, definition), token)));
            var state = new BlobEmptySlotPublicState(new(partition, definition.Name, "same-empty-blob"));
            state.Begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(state.First, token));
            state.OriginalCurrent = state.Begun.Value;
            await BlobEmptySlotPublicAssertions.ReceiptAsync(state.Begun.Receipt, state.First.CommandId, state.Blob);
            await Assert.That(state.Begun.Value.Blob).IsEqualTo(state.Blob);
            await Assert.That(state.Begun.Value.UploadId).IsEqualTo(state.First.UploadId);
            await Assert.That(state.Begun.Value.ExpectedRevision).IsEqualTo(0L);
            await Assert.That(state.Begun.Value.DeclaredLength).IsEqualTo(0L);
            await Assert.That(state.Begun.Value.StoredBytes).IsEqualTo(0L);
            await Assert.That(state.Begun.Value.NextOrdinal).IsEqualTo(0);
            await Assert.That(state.Begun.Value.Status).IsEqualTo(BlobUploadStatus.Active);
            await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
            await BlobEmptySlotPublicAssertions.RefusedAsync(callers, state, state.InitialRefused, token);
            await AbortAndReclaimAsync(callers, state, token);
            await BlobEmptySlotPublicOperations.PublishAsync(callers, state, token);
            await BlobEmptySlotPublicOperations.ReplayAsync(callers, state, token);
            return true;
        }, token);
    }

    private static async Task AbortAndReclaimAsync(RequestCqrsClusterCallers callers, BlobEmptySlotPublicState state, CancellationToken token)
    {
        state.Abort = new(Guid.NewGuid(), state.Blob, state.First.UploadId);
        state.Aborted = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_abort_upload", state.Abort, token))).Value;
        state.OriginalCurrent = state.Begun.Value with
        {
            Status = BlobUploadStatus.Aborted
        };
        await SqlClusterProtocol.EqualAsync(state.OriginalCurrent, state.Aborted.Value);
        await BlobEmptySlotPublicAssertions.ReceiptAsync(state.Aborted.Receipt, state.Abort.CommandId, state.Blob);
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
        await BlobEmptySlotPublicAssertions.RefusedAsync(callers, state, state.RetainedRefused, token);
        state.Reclaim = new(Guid.NewGuid(), state.Blob, state.First.UploadId, 1);
        state.Reclaimed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReclaimBlobAsync(state.Reclaim, token));
        await SqlClusterProtocol.EqualAsync(new BlobReclaimResult(state.First.UploadId, 0, 0, 0L, true), state.Reclaimed.Value);
        await BlobEmptySlotPublicAssertions.ReceiptAsync(state.Reclaimed.Receipt, state.Reclaim.CommandId, state.Blob);
        state.OriginalCurrent = null;
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
        await BlobEmptySlotPublicOperations.ReclaimReplayAsync(callers, state, token);
        await BlobEmptySlotPublicAssertions.RefusedAsync(callers, state, state.InitialRefused, token);
        await BlobEmptySlotPublicAssertions.RefusedAsync(callers, state, state.RetainedRefused, token);
    }
}
