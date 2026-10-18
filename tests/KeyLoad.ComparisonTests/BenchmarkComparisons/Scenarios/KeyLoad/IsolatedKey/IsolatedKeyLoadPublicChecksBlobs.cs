using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-004/005: real SDK and official MCP callers publish exact two-part data only after valid integrity checks.</summary>
internal static class IsolatedKeyLoadPublicChecksBlobs
{
    private const int TailLength = 9;
    private const byte FirstByte = 42;
    private const byte TailByte = 43;
    private const string InvalidHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly byte[] ExpectedRange = [FirstByte, FirstByte, FirstByte, TailByte, TailByte, TailByte, TailByte];

    internal static async Task VerifyAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicChecksMcp mcp,
        IsolatedKeyLoadPublicChecksScenario scenario, CancellationToken token)
    {
        var blob = scenario.Blob;
        var uploadId = Guid.NewGuid();
        var firstBytes = new byte[BlobLimits.RawPartBytes];
        Array.Fill(firstBytes, FirstByte);
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, uploadId, firstBytes.Length + TailLength, 0);
        var begun = await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await sdk.BeginBlobUploadAsync(begin, token));
        await IsolatedKeyLoadPublicChecksAssertions.ReceiptAsync(begun.Receipt, begin.CommandId, 0, blob.Partition, scenario.RequiredCopies);
        await Assert.That(begun.Value.UploadId).IsEqualTo(uploadId);
        await Assert.That(begun.Value.Blob).IsEqualTo(blob);
        await VerifyInvalidPartAsync(sdk, mcp, blob, uploadId, firstBytes, begun.Value, token);
        var first = new WriteBlobPartRequest(Guid.NewGuid(), blob, uploadId, 0, firstBytes, BlobIntegrity.PartHash(firstBytes));
        var accepted = await mcp.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(IsolatedKeyLoadPublicChecksProtocol.BlobPart, first, token);
        await IsolatedKeyLoadPublicChecksAssertions.ReceiptAsync(accepted.Receipt, first.CommandId, 0, blob.Partition, scenario.RequiredCopies);
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(accepted,
            await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await sdk.WriteBlobPartAsync(first, token)));
        var metadata = await PublishTailAsync(sdk, mcp, blob, uploadId, first, begun.Value.IntegrityHash, scenario.RequiredCopies, token);
        await VerifyRangeAsync(sdk, mcp, metadata, token);
    }

    private static async Task VerifyInvalidPartAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicChecksMcp mcp,
        BlobRef blob, Guid uploadId, byte[] bytes, BlobUploadInfo initial, CancellationToken token)
    {
        var invalid = new WriteBlobPartRequest(Guid.NewGuid(), blob, uploadId, 0, bytes, InvalidHash);
        await mcp.ErrorAsync(IsolatedKeyLoadPublicChecksProtocol.BlobPart, invalid, ErrorCode.Validation, token);
        await IsolatedKeyLoadPublicChecksAssertions.ErrorAsync(await sdk.WriteBlobPartAsync(invalid, token), ErrorCode.Validation);
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(initial,
            (await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await sdk.GetBlobUploadInfoAsync(new(blob, uploadId), token))).Upload);
        await Assert.That((await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await sdk.GetBlobMetadataAsync(new(blob), token))).Metadata).IsNull();
        await Assert.That((await mcp.SuccessAsync<BlobMetadataReadResult>(IsolatedKeyLoadPublicChecksProtocol.BlobMetadata,
            new BlobMetadataRequest(blob), token)).Metadata).IsNull();
    }

    private static async Task<BlobMetadata> PublishTailAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicChecksMcp mcp,
        BlobRef blob, Guid uploadId, WriteBlobPartRequest first, string initialHash, int expectedCopies, CancellationToken token)
    {
        var tailBytes = new byte[TailLength];
        Array.Fill(tailBytes, TailByte);
        var tail = new WriteBlobPartRequest(Guid.NewGuid(), blob, uploadId, 1, tailBytes, BlobIntegrity.PartHash(tailBytes));
        var accepted = await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await sdk.WriteBlobPartAsync(tail, token));
        var hash = BlobIntegrity.NextHash(initialHash, first.Ordinal, first.Bytes.Length, first.Sha256);
        hash = BlobIntegrity.NextHash(hash, tail.Ordinal, tail.Bytes.Length, tail.Sha256);
        await Assert.That(accepted.Value.IntegrityHash).IsEqualTo(hash);
        var invalid = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, uploadId, InvalidHash);
        await IsolatedKeyLoadPublicChecksAssertions.ErrorAsync(await sdk.CompleteBlobUploadAsync(invalid, token), ErrorCode.Validation);
        await Assert.That((await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await sdk.GetBlobMetadataAsync(new(blob), token))).Metadata).IsNull();
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, uploadId, hash);
        var published = await mcp.SuccessAsync<BlobCommitResult<BlobMetadata>>(SqlOperationProtocol.ToolName,
            IsolatedKeyLoadPublicChecksProtocol.Call(blob.Partition, IsolatedKeyLoadPublicChecksProtocol.BlobComplete, complete), token);
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(published,
            await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await sdk.CompleteBlobUploadAsync(complete, token)));
        await IsolatedKeyLoadPublicChecksAssertions.ReceiptAsync(published.Receipt, complete.CommandId, 0, blob.Partition, expectedCopies);
        await Assert.That(published.Value.VersionId).IsEqualTo((Guid?)uploadId);
        await Assert.That(published.Value.Revision).IsEqualTo(1L);
        await Assert.That(published.Value.Blob).IsEqualTo(blob);
        await Assert.That(published.Value.Length).IsEqualTo((long)BlobLimits.RawPartBytes + TailLength);
        await Assert.That(published.Value.PartCount).IsEqualTo(2);
        await Assert.That(published.Value.IntegrityHash).IsEqualTo(hash);
        return published.Value;
    }

    private static async Task VerifyRangeAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicChecksMcp mcp,
        BlobMetadata metadata, CancellationToken token)
    {
        var request = new BlobReadRequest(metadata.Blob, metadata.Revision, BlobLimits.RawPartBytes - 3, ExpectedRange.Length);
        var native = await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await sdk.ReadBlobRangeAsync(request, token));
        var official = await mcp.SuccessAsync<BlobReadResult>(IsolatedKeyLoadPublicChecksProtocol.BlobRange, request, token);
        await Assert.That(native.Bytes.Span.SequenceEqual(ExpectedRange)).IsTrue();
        await Assert.That(official.Bytes.Span.SequenceEqual(ExpectedRange)).IsTrue();
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(metadata, native.Metadata);
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(native, official);
        await Assert.That(native.Offset).IsEqualTo(request.Offset);
        var list = await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(
            await sdk.ListBlobsAsync(new(metadata.Blob.Partition, metadata.Blob.Resource), token));
        await Assert.That(list.Items).HasSingleItem();
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(metadata, list.Items[0]);
    }
}
