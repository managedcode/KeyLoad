using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobLiveListR43Publication
{
    internal static async Task PublishAsync(RequestCqrsClusterCallers callers, BlobLiveListR43State state,
        int index, CancellationToken token)
    {
        var blob = new BlobRef(state.Partition, RelationalSqlClusterTokens.Blobs, BlobLiveListR43State.Id(index));
        var bytes = BlobLiveListR43State.Bytes(index);
        var published = await BlobBoundedRetirementPublicPublication.CreateAsync(callers, blob, 0L, bytes, token);
        var partHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        await Assert.That(published.Parts.Length).IsEqualTo(1);
        await Assert.That(published.Parts[0].Sha256).IsEqualTo(partHash);
        var hash = Chain(published.Begun.Value.IntegrityHash, bytes.Length, partHash);
        var expected = new BlobMetadata(blob, 1L, published.Begin.UploadId, 2L, 1, hash,
            new RowAccess(), published.Completed.Value.UpdatedAt);
        await SqlClusterProtocol.EqualAsync(expected, published.Completed.Value);
        await ReceiptAsync(published.Begin.CommandId, published.Begun.Receipt);
        await ReceiptAsync(published.Parts[0].CommandId, published.Written[0].Receipt);
        await ReceiptAsync(published.Complete.CommandId, published.Completed.Receipt);
        state.Versions[index] = published;
    }
    private static string Chain(string previous, int length, string part)
    {
        Span<byte> input = stackalloc byte[72];
        Convert.FromHexString(previous).AsSpan().CopyTo(input);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(input.Slice(32, 4), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(input.Slice(36, 4), length);
        Convert.FromHexString(part).AsSpan().CopyTo(input[40..]);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(input));
    }
    internal static async Task ReceiptAsync(Guid commandId, CommitReceipt receipt)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
