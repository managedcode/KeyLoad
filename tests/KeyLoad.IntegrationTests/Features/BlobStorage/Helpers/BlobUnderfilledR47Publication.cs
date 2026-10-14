using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobUnderfilledR47Publication
{
    internal static async Task FinishAsync(RequestCqrsRf3Callers callers, BlobUnderfilledR47State owned, CancellationToken token)
    {
        var state = owned.Original;
        state.Tail = new(Guid.NewGuid(), state.Begin.Blob, state.Begin.UploadId, 1,
            BlobPartIdentityR40Corpus.Bytes(65536, 4), BlobPartIdentityR40Corpus.TailSha);
        state.WrittenTail = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync(BlobPartIdentityR40Replay.PartTool, state.Tail, token))).Value;
        var chain = BlobPartIdentityR40Corpus.Next(state.WrittenFirst.Value.IntegrityHash, 1, 4, BlobPartIdentityR40Corpus.TailSha);
        await SqlRf3Protocol.EqualAsync(state.Begun.Value with
        {
            NextOrdinal = 2,
            StoredBytes = 65540L,
            IntegrityHash = chain
        }, state.WrittenTail.Value);
        await BlobPartIdentityR40Assertions.ReceiptAsync(state.WrittenTail.Receipt, state.Tail.CommandId, state);
        await BlobPartIdentityR40Assertions.CurrentAsync(callers, state, token);
        await BlobPartIdentityR40Replay.RunAsync(callers, state, token);
        await BlobTailShapeR48Refusals.BothAsync(callers, owned.TailShape, token);
        await BlobUnderfilledR47Refusals.BothAsync(callers, owned, token);
        state.Complete = new(Guid.NewGuid(), state.Begin.Blob, state.Begin.UploadId, chain);
        await SqlRf3Protocol.EqualAsync(owned.Premature.ExpectedIntegrityHash, chain);
        await Assert.That(state.Complete.CommandId).IsNotEqualTo(owned.Premature.CommandId);
        state.Completed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(state.Complete, token));
        await SqlRf3Protocol.EqualAsync(new BlobMetadata(state.Begin.Blob, 1L, state.Begin.UploadId, 65540L, 2,
            chain, new RowAccess(), state.Completed.Value.UpdatedAt, false), state.Completed.Value);
        await BlobPartIdentityR40Assertions.ReceiptAsync(state.Completed.Receipt, state.Complete.CommandId, state);
        await BlobPartIdentityR40Assertions.CurrentAsync(callers, state, token);
    }
}
