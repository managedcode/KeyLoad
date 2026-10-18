using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPrincipalRevokedR44Publication
{
    internal static async Task RequireAsync(BlobBoundedRetirementPublicPublication.Published p)
    {
        await Assert.That(p.Bytes.AsSpan().SequenceEqual(
            BlobPrincipalRevokedR44Corpus.Bytes(0L, BlobPrincipalRevokedR44Corpus.Length))).IsTrue();
        await Assert.That(p.Parts.Length).IsEqualTo(2);
        await Assert.That(p.Written.Length).IsEqualTo(2);
        var hash = p.Begun.Value.IntegrityHash;
        await SqlClusterProtocol.EqualAsync(new BlobUploadInfo(p.Begin.Blob, p.Begin.UploadId, 65553L,
            0L, 0, 0L, p.Begun.Value.ExpiresAt, BlobUploadStatus.Active, hash), p.Begun.Value);
        await BlobLiveListR43Publication.ReceiptAsync(p.Begin.CommandId, p.Begun.Receipt);
        for (var index = 0; index < 2; index++)
        {
            await Assert.That(p.Parts[index].Ordinal).IsEqualTo(index);
            await Assert.That(p.Parts[index].Sha256).IsEqualTo(BlobPrincipalRevokedR44Corpus.PartHashes[index]);
            var length = index == 0 ? 65536 : 17;
            hash = BlobPrincipalRevokedR44Corpus.Next(hash, index, length, BlobPrincipalRevokedR44Corpus.PartHashes[index]);
            await SqlClusterProtocol.EqualAsync(p.Begun.Value with
            {
                NextOrdinal = index + 1,
                StoredBytes = index == 0 ? 65536L : 65553L,
                IntegrityHash = hash
            }, p.Written[index].Value);
            await BlobLiveListR43Publication.ReceiptAsync(p.Parts[index].CommandId, p.Written[index].Receipt);
        }
        await SqlClusterProtocol.EqualAsync(new BlobMetadata(p.Begin.Blob, 1L, p.Begin.UploadId,
            65553L, 2, hash, new RowAccess(), p.Completed.Value.UpdatedAt), p.Completed.Value);
        await BlobLiveListR43Publication.ReceiptAsync(p.Complete.CommandId, p.Completed.Receipt);
    }
}
