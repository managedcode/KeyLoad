using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobScopedBytesR42Publication
{
    internal static async Task WriteAsync(RequestCqrsClusterCallers callers, BlobScopedBytesR42State state, BlobScopedBytesR42State[] states, CancellationToken token)
    {
        state.Begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(state.Begin, token));
        state.Current = new(state.Blob, state.Begin.UploadId, BlobScopedBytesR42Corpus.Length, 0L, 0, 0L, state.Begun.Value.ExpiresAt, BlobUploadStatus.Active, state.Begun.Value.IntegrityHash);
        await SqlClusterProtocol.EqualAsync(state.Current, state.Begun.Value);
        await BlobScopedBytesR42Assertions.ReceiptAsync(state.Begun.Receipt, state.Begin.CommandId, state);
        await BlobScopedBytesR42Assertions.CurrentAsync(callers, states, token);
        var hash = state.Begun.Value.IntegrityHash;
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            var bytes = BlobScopedBytesR42Corpus.Bytes(state.Index, (long)ordinal * 65536, ordinal == 0 ? 65536 : 17);
            var sha = BlobScopedBytesR42Corpus.PartHashes[state.Index][ordinal];
            await Assert.That(BlobScopedBytesR42Corpus.Hash(bytes)).IsEqualTo(sha);
            state.Parts[ordinal] = new(Guid.NewGuid(), state.Blob, state.Begin.UploadId, ordinal, bytes, sha);
            state.Written[ordinal] = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_write_part", state.Parts[ordinal], token))).Value;
            hash = BlobScopedBytesR42Corpus.Next(hash, ordinal, bytes.Length, sha);
            state.Current = state.Begun.Value with
            {
                NextOrdinal = ordinal + 1,
                StoredBytes = ordinal == 0 ? 65536L : 65553L,
                IntegrityHash = hash
            };
            await SqlClusterProtocol.EqualAsync(state.Current, state.Written[ordinal].Value);
            await BlobScopedBytesR42Assertions.ReceiptAsync(state.Written[ordinal].Receipt, state.Parts[ordinal].CommandId, state);
            await BlobScopedBytesR42Assertions.CurrentAsync(callers, states, token);
        }
    }
    internal static async Task CompleteAsync(RequestCqrsClusterCallers callers, BlobScopedBytesR42State state, BlobScopedBytesR42State[] states, CancellationToken token)
    {
        state.Complete = new(Guid.NewGuid(), state.Blob, state.Begin.UploadId, state.Written[1].Value.IntegrityHash);
        state.Published = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(state.Complete, token));
        var expected = new BlobMetadata(state.Blob, 1L, state.Begin.UploadId, 65553L, 2, state.Written[1].Value.IntegrityHash, new RowAccess(), state.Published.Value.UpdatedAt);
        await SqlClusterProtocol.EqualAsync(expected, state.Published.Value);
        state.Current = state.Written[1].Value with
        {
            Status = BlobUploadStatus.Complete
        };
        await BlobScopedBytesR42Assertions.ReceiptAsync(state.Published.Receipt, state.Complete.CommandId, state);
        await BlobScopedBytesR42Assertions.CurrentAsync(callers, states, token);
    }
}
