using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartialReadCancellationR45Publication
{
    internal static async Task RequireAsync(BlobBoundedRetirementPublicPublication.Published p,
        AtomicPartitionPlacementResolution placement)
    {
        await Assert.That(p.Bytes.AsSpan().SequenceEqual(
            BlobPartialReadCancellationR45Corpus.Bytes(0L, BlobPartialReadCancellationR45Corpus.Length))).IsTrue();
        await Assert.That(p.Parts.Length).IsEqualTo(2);
        await Assert.That(p.Written.Length).IsEqualTo(2);
        var hash = p.Begun.Value.IntegrityHash;
        await SqlRf3Protocol.EqualAsync(new BlobUploadInfo(p.Begin.Blob, p.Begin.UploadId, 65553L,
            0L, 0, 0L, p.Begun.Value.ExpiresAt, BlobUploadStatus.Active, hash), p.Begun.Value);
        await BlobLiveListR43Publication.ReceiptAsync(placement, p.Begin.CommandId, p.Begun.Receipt);
        for (var index = 0; index < 2; index++)
        {
            await Assert.That(p.Parts[index].Ordinal).IsEqualTo(index);
            await Assert.That(p.Parts[index].Sha256).IsEqualTo(BlobPartialReadCancellationR45Corpus.PartHashes[index]);
            var length = index == 0 ? 65536 : 17;
            hash = BlobPartialReadCancellationR45Corpus.Next(hash, index, length, BlobPartialReadCancellationR45Corpus.PartHashes[index]);
            await SqlRf3Protocol.EqualAsync(p.Begun.Value with
            {
                NextOrdinal = index + 1,
                StoredBytes = index == 0 ? 65536L : 65553L,
                IntegrityHash = hash
            }, p.Written[index].Value);
            await BlobLiveListR43Publication.ReceiptAsync(placement, p.Parts[index].CommandId, p.Written[index].Receipt);
        }
        await SqlRf3Protocol.EqualAsync(new BlobMetadata(p.Begin.Blob, 1L, p.Begin.UploadId,
            65553L, 2, hash, new RowAccess(), p.Completed.Value.UpdatedAt), p.Completed.Value);
        await BlobLiveListR43Publication.ReceiptAsync(placement, p.Complete.CommandId, p.Completed.Receipt);
    }
}
