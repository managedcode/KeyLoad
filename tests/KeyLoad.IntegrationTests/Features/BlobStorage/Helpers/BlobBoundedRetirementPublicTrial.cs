using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobBoundedRetirementPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async callers =>
            {
                var scenario = await RelationalSqlRf3Scenario.CreateAsync(callers.Sdk, token);
                var blob = new BlobRef(scenario.Partition, RelationalSqlRf3Tokens.Blobs, "bounded-retirement");
                var bytes = Enumerable.Range(0, BlobLimits.RawPartBytes + 1).Select(i => (byte)(i % 239)).ToArray();
                var old = await BlobBoundedRetirementPublicPublication.CreateAsync(callers, blob, 0L, bytes, token);
                await Assert.That(old.Completed.Value.PartCount).IsEqualTo(2);
                await Assert.That(old.Completed.Value.Length).IsEqualTo((long)BlobLimits.RawPartBytes + 1L);
                await BlobBoundedRetirementPublicAssertions.CurrentAsync(callers, old, token);
                var live = new ReclaimBlobRequest(Guid.NewGuid(), blob, old.Begin.UploadId, 1);
                await BlobBoundedRetirementPublicAssertions.RefusedAsync(callers, live, ErrorCode.Conflict,
                    () => BlobBoundedRetirementPublicAssertions.CurrentAsync(callers, old, token), token);
                await RetireAsync(callers, old, live, token);
                return true;
            }, token);
    }

    private static async Task RetireAsync(RequestCqrsRf3Callers callers,
        BlobBoundedRetirementPublicPublication.Published old, ReclaimBlobRequest live, CancellationToken token)
    {
        var blob = old.Begin.Blob;
        var delete = new DeleteBlobRequest(Guid.NewGuid(), blob, 1L);
        var deleted = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await callers.Mcp.CallAsync("keyload_blobs_delete", delete, token))).Value;
        var tombstone = old.Completed.Value with
        {
            Revision = 2L,
            VersionId = null,
            Length = 0L,
            PartCount = 0,
            IntegrityHash = null,
            Deleted = true,
            UpdatedAt = deleted.Value.UpdatedAt
        };
        await SqlRf3Protocol.EqualAsync(tombstone, deleted.Value);
        await Assert.That(deleted.Receipt.CommandId).IsEqualTo(delete.CommandId);
        await Assert.That(deleted.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var first = new ReclaimBlobRequest(Guid.NewGuid(), blob, old.Begin.UploadId, 1);
        var reclaimed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReclaimBlobAsync(first, token));
        await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(old.Begin.UploadId, 1, 1, (long)BlobLimits.RawPartBytes, false), reclaimed.Value);
        await Assert.That(reclaimed.Receipt.CommandId).IsEqualTo(first.CommandId);
        await Assert.That(reclaimed.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var remaining = old.Written[1].Value with { Status = BlobUploadStatus.Complete, StoredBytes = 1L };
        async Task ObservePartial()
        {
            await BlobBoundedRetirementPublicAssertions.HeadAsync(callers, tombstone, token);
            await BlobBoundedRetirementPublicAssertions.UploadAsync(callers, blob, old.Begin.UploadId, remaining, token);
        }
        await ObservePartial();
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_reclaim", first, reclaimed,
            ct => callers.Sdk.ReclaimBlobAsync(first, ct), ObservePartial, token);
        await BlobBoundedRetirementPublicReplay.PublicationAsync(callers, old, ObservePartial, token);
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_delete", delete, deleted,
            ct => callers.Sdk.DeleteBlobAsync(delete, ct), ObservePartial, token);
        var final = new ReclaimBlobRequest(Guid.NewGuid(), blob, old.Begin.UploadId, 1);
        var finished = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobReclaimResult>>(
            await callers.Mcp.CallAsync("keyload_blobs_reclaim", final, token))).Value;
        await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(old.Begin.UploadId, 1, 0, 1L, true), finished.Value);
        await ContinueAsync(callers, old, live, first, final, finished, tombstone, token);
    }

    private static async Task ContinueAsync(RequestCqrsRf3Callers callers,
        BlobBoundedRetirementPublicPublication.Published old, ReclaimBlobRequest live, ReclaimBlobRequest first,
        ReclaimBlobRequest final, BlobCommitResult<BlobReclaimResult> finished, BlobMetadata tombstone, CancellationToken token)
    {
        async Task ObserveEmpty()
        {
            await BlobBoundedRetirementPublicAssertions.HeadAsync(callers, tombstone, token);
            await BlobBoundedRetirementPublicAssertions.UploadAsync(callers, old.Begin.Blob, old.Begin.UploadId, null, token);
        }
        await ObserveEmpty();
        await BlobBoundedRetirementPublicAssertions.LifetimeAsync(callers, old, first, ObserveEmpty, token);
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_reclaim", final, finished,
            ct => callers.Sdk.ReclaimBlobAsync(final, ct), ObserveEmpty, token);
        var healthy = await BlobBoundedRetirementPublicPublication.CreateAsync(callers, old.Begin.Blob, 2L, [83], token);
        Task ObserveHealthy() => BlobBoundedRetirementPublicAssertions.CurrentAsync(callers, healthy, token);
        await ObserveHealthy();
        await BlobBoundedRetirementPublicAssertions.RefusedAsync(callers, live, ErrorCode.Conflict, ObserveHealthy, token);
        await BlobBoundedRetirementPublicAssertions.LifetimeAsync(callers, old, first, ObserveHealthy, token);
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_reclaim", final, finished,
            ct => callers.Sdk.ReclaimBlobAsync(final, ct), ObserveHealthy, token);
        await BlobBoundedRetirementPublicReplay.PublicationAsync(callers, healthy, ObserveHealthy, token);
        await Assert.That(finished.Receipt.CommandId).IsEqualTo(final.CommandId);
        await Assert.That(finished.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
    }
}
