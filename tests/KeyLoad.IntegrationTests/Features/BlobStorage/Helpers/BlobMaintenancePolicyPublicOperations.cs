using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobMaintenancePolicyPublicOperations
{
    internal static async Task PersistAsync(RequestCqrsRf3Callers administrator, PrincipalRecord original,
        Capability excluded, long epoch, CancellationToken token)
    {
        var expected = original with
        {
            Grants = [.. original.Grants.Select(grant => grant with { Capabilities = grant.Capabilities & ~excluded })],
            PolicyEpoch = epoch
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }

    internal static async Task DeleteAsync(RequestCqrsRf3Callers creator, BlobMaintenancePolicyPublicState state, CancellationToken token)
    {
        state.Delete = new(Guid.NewGuid(), state.Blob, 1L);
        state.Deleted = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await creator.Mcp.CallAsync("keyload_blobs_delete", state.Delete, token))).Value;
        state.Tombstone = state.Original.Completed.Value with
        {
            Revision = 2L,
            VersionId = null,
            Length = 0L,
            PartCount = 0,
            IntegrityHash = null,
            Deleted = true,
            UpdatedAt = state.Deleted.Value.UpdatedAt
        };
        await SqlRf3Protocol.EqualAsync(state.Tombstone, state.Deleted.Value);
        await Assert.That(state.Deleted.Receipt.CommandId).IsEqualTo(state.Delete.CommandId);
        await BlobMaintenancePolicyPublicAssertions.ReceiptAsync(state.Blob, state.Placement, state.Deleted.Receipt);
        await Assert.That(state.Deleted.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        Task Observe() => BlobMaintenancePolicyPublicAssertions.RetiredAsync(creator, state, token);
        await Observe();
        await BlobBoundedRetirementPublicReplay.MatchingAsync(creator, "keyload_blobs_delete", state.Delete, state.Deleted,
            ct => creator.Sdk.DeleteBlobAsync(state.Delete, ct), Observe, token);
    }

    internal static async Task ReclaimAndPublishAsync(RequestCqrsRf3Callers creator, BlobMaintenancePolicyPublicState state, CancellationToken token)
    {
        state.Reclaim = new(Guid.NewGuid(), state.Blob, state.Original.Begin.UploadId, 1);
        state.Reclaimed = await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.ReclaimBlobAsync(state.Reclaim, token));
        await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(state.Original.Begin.UploadId, 1, 0, 4L, true), state.Reclaimed.Value);
        await Assert.That(state.Reclaimed.Receipt.CommandId).IsEqualTo(state.Reclaim.CommandId);
        await BlobMaintenancePolicyPublicAssertions.ReceiptAsync(state.Blob, state.Placement, state.Reclaimed.Receipt);
        await Assert.That(state.Reclaimed.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await BlobBoundedRetirementPublicAssertions.HeadAsync(creator, state.Tombstone, token);
        await BlobBoundedRetirementPublicAssertions.UploadAsync(creator, state.Blob, state.Original.Begin.UploadId, null, token);
        state.Healthy = await BlobBoundedRetirementPublicPublication.CreateAsync(creator, state.Blob, 2L, [71, 73, 79, 83], token);
        await Assert.That(state.Healthy.Completed.Value.Revision).IsEqualTo(3L);
        await BlobMaintenancePolicyPublicVerification.FinalAsync(creator, state, token);
    }
}
