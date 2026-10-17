using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRestoredReclaimPublicAssertions
{
    private const string Detail = "detail";
    private const string Capacity = "The blob reservation quota is exhausted.";
    private const string SafeCapacity = "The operation exceeds the available resource capacity.";
    internal static async Task CurrentAsync(KeyLoadClient sdk, McpOfficialClient official,
        BlobRestoredReclaimPublicState state, bool final, CancellationToken token)
    {
        await BlobArchiveActivePublicAssertions.CurrentAsync(sdk, official, state.Published, token);
        if (state.Healthy is { } healthy)
        {
            await BlobArchiveActivePublicAssertions.CurrentAsync(sdk, official, healthy, token);
        }
        if (state.Healthy is null)
        {
            var metadata = new BlobMetadataRequest(state.Active.Begin.Blob);
            await Assert.That(await PublicReadAssertions.MetadataAsync(await sdk.GetBlobMetadataAsync(metadata, token))).IsNull();
            await Assert.That((await PublicReadAssertions.MetadataAsync(await official.CallAsync("keyload_blobs_metadata", metadata, token))).Value).IsNull();
        }
        var request = new BlobUploadInfoRequest(state.Active.Begin.Blob, state.Active.Begin.UploadId);
        var expected = final ? null : state.Active.Written.Last().Value with
        {
            Status = BlobUploadStatus.Aborted,
            StoredBytes = state.FirstResult is null ? 131072L : 65536L
        };
        await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.UploadAsync(await sdk.GetBlobUploadInfoAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.UploadAsync(await official.CallAsync("keyload_blobs_upload_info", request, token))).Value);
    }

    internal static async Task QuotaAsync(KeyLoadClient sdk, McpOfficialClient official,
        BlobRestoredReclaimPublicState state, BeginBlobUploadRequest request, CancellationToken token)
    {
        var denied = await sdk.BeginBlobUploadAsync(request, token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.ResourceExhausted));
        await Assert.That(denied.Problem?.Detail).IsEqualTo(Capacity);
        await CurrentAsync(sdk, official, state, false, token);
        var reply = await official.CallAsync("keyload_blobs_begin_upload", request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.ResourceExhausted, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error).GetProperty(Detail).GetString()).IsEqualTo(SafeCapacity);
        await CurrentAsync(sdk, official, state, false, token);
        var upload = new BlobUploadInfoRequest(request.Blob, request.UploadId);
        await Assert.That(await PublicReadAssertions.UploadAsync(await sdk.GetBlobUploadInfoAsync(upload, token))).IsNull();
        await Assert.That((await PublicReadAssertions.UploadAsync(await official.CallAsync("keyload_blobs_upload_info", upload, token))).Value).IsNull();
    }

    internal static async Task ResultAsync(BlobCommitResult<BlobReclaimResult> actual,
        ReclaimBlobRequest request, AtomicPartitionPlacementResolution placement, bool final)
    {
        await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(request.UploadId, 1, final ? 0 : 1, 65536L, final), actual.Value);
        await Assert.That(actual.Receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(actual.Receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(actual.Receipt.Token.AtomicPartitionId).IsEqualTo(request.Blob.Partition.AtomicPartitionId);
        await Assert.That(actual.Receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(actual.Receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Receipt.Mutations).IsEmpty();
    }
}
