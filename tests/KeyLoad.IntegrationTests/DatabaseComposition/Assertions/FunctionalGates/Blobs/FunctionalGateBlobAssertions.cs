using System.Security.Cryptography;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.BlobStorage;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateBlobAssertions
{
    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid command, BlobRef blob)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Token.Incarnation == Guid.Empty).IsFalse();
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).IsEmpty();
    }

    internal static async Task HeadAsync(FunctionalGateContext context, BlobMetadata expected, CancellationToken token)
    {
        var request = new BlobMetadataRequest(expected.Blob);
        await SqlClusterProtocol.EqualAsync(expected,
            (await context.SdkAsync(context.Reader.Sdk.GetBlobMetadataAsync(request, token))).Metadata);
        await SqlClusterProtocol.EqualAsync(expected, (await context.McpAsync<BlobMetadataReadResult>(
            context.Reader, "keyload_blobs_metadata", request, token)).Metadata);
    }

    internal static async Task ContentAsync(FunctionalGateContext context,
        BlobMetadata metadata, byte[] bytes, CancellationToken token)
    {
        await HeadAsync(context, metadata, token);
        var sdk = new byte[bytes.Length];
        var mcp = new byte[bytes.Length];
        for (var offset = 0; offset < bytes.Length; offset += BlobLimits.MaxRangeBytes)
        {
            var count = Math.Min(BlobLimits.MaxRangeBytes, bytes.Length - offset);
            var request = new BlobReadRequest(metadata.Blob, metadata.Revision, offset, count);
            var expected = new BlobReadResult(metadata, offset, bytes.AsMemory(offset, count));
            var read = await context.SdkAsync(context.Reader.Sdk.ReadBlobRangeAsync(request, token));
            var official = await context.McpAsync<BlobReadResult>(context.Reader, "keyload_blobs_read_range", request, token);
            await SqlClusterProtocol.EqualAsync(expected, read);
            await SqlClusterProtocol.EqualAsync(expected, official);
            read.Bytes.CopyTo(sdk.AsMemory(offset));
            official.Bytes.CopyTo(mcp.AsMemory(offset));
        }
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(sdk)))
            .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(mcp)))
            .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        await RangeAsync(context, metadata, bytes, BlobLimits.RawPartBytes - 11, 29, token);
        await RangeAsync(context, metadata, bytes, bytes.Length, 0, token);
    }

    private static async Task RangeAsync(FunctionalGateContext context, BlobMetadata metadata,
        byte[] bytes, int offset, int count, CancellationToken token)
    {
        var request = new BlobReadRequest(metadata.Blob, metadata.Revision, offset, count);
        var expected = new BlobReadResult(metadata, offset, bytes.AsMemory(offset, count));
        await SqlClusterProtocol.EqualAsync(expected, await context.SdkAsync(context.Reader.Sdk.ReadBlobRangeAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected,
            await context.McpAsync<BlobReadResult>(context.Reader, "keyload_blobs_read_range", request, token));
    }

    internal static async Task DeniedAsync(FunctionalGateContext context,
        FunctionalGateBlobVersion published, CancellationToken token)
    {
        var request = new DeleteBlobRequest(Guid.NewGuid(), published.Begin.Blob, 1L);
        await BlobBoundedRetirementPublicAssertions.FailedAsync(
            await context.Reader.Sdk.DeleteBlobAsync(request, token), ErrorCode.PermissionDenied);
        await ContentAsync(context, published.Published.Value, published.Bytes, token);
        await McpCallerAssertions.ErrorAsync(await context.Reader.Mcp.CallAsync("keyload_blobs_delete", request, token),
            ErrorCode.PermissionDenied, dispatched: true);
        await ContentAsync(context, published.Published.Value, published.Bytes, token);
    }

    internal static async Task BaselineAsync(FunctionalGateContext context, BlobRef blob,
        int index, CancellationToken token)
    {
        var metadata = (await context.SdkAsync(context.Reader.Sdk.GetBlobMetadataAsync(new(blob), token))).Metadata;
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.VersionId.HasValue).IsTrue();
        var owner = await context.SdkAsync(context.Administrator.Sdk.StatusAsync(token));
        var bytes = FunctionalGateBlobUpload.Content(context, blob, -1 - index);
        var initial = BlobIntegrity.InitialHash(owner.Incarnation, blob, metadata.VersionId!.Value, bytes.Length);
        var first = bytes.AsSpan(0, BlobLimits.RawPartBytes).ToArray();
        var last = bytes.AsSpan(BlobLimits.RawPartBytes).ToArray();
        var chain = BlobIntegrity.NextHash(initial, 0, first.Length, Convert.ToHexStringLower(SHA256.HashData(first)));
        chain = BlobIntegrity.NextHash(chain, 1, last.Length, Convert.ToHexStringLower(SHA256.HashData(last)));
        var expected = new BlobMetadata(blob, 1L, metadata.VersionId, bytes.Length, 2, chain, new RowAccess(), metadata.UpdatedAt);
        await SqlClusterProtocol.EqualAsync(expected, metadata);
        await ContentAsync(context, expected, bytes, token);
    }

    internal static async Task RetiredAsync(FunctionalGateContext context,
        FunctionalGateBlobVersion published, BlobMetadata tombstone, CancellationToken token)
    {
        await HeadAsync(context, tombstone, token);
        var stale = new BlobReadRequest(published.Begin.Blob, 1L, 0L, 1);
        await BlobBoundedRetirementPublicAssertions.FailedAsync(
            await context.Reader.Sdk.ReadBlobRangeAsync(stale, token), ErrorCode.NotFound);
        await McpCallerAssertions.ErrorAsync(await context.Reader.Mcp.CallAsync("keyload_blobs_read_range", stale, token),
            ErrorCode.NotFound, dispatched: true);
        await HeadAsync(context, tombstone, token);
    }
}
