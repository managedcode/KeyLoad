using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartIdentityR40Operations
{
    private const string ObjectId = "original-part-identity-r40";

    internal static async Task<BlobPartIdentityR40State> StartAsync(RequestCqrsRf3Callers callers, CancellationToken token)
    {
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(callers.Sdk, token);
        var blob = new BlobRef(scenario.Partition, RelationalSqlRf3Tokens.Blobs, ObjectId);
        var request = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await callers.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new BlobPartIdentityR40State(new(Guid.NewGuid(), blob, Guid.NewGuid(), BlobPartIdentityR40Corpus.Length, 0L), placement, outbox);
        state.Begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(state.Begin, token));
        await SqlRf3Protocol.EqualAsync(new BlobUploadInfo(blob, state.Begin.UploadId, 65540L, 0L, 0, 0L,
            state.Begun.Value.ExpiresAt, BlobUploadStatus.Active, state.Begun.Value.IntegrityHash), state.Begun.Value);
        await BlobPartIdentityR40Assertions.ReceiptAsync(state.Begun.Receipt, state.Begin.CommandId, state);
        state.First = new(Guid.NewGuid(), blob, state.Begin.UploadId, 0,
            BlobPartIdentityR40Corpus.Bytes(0, 65536), BlobPartIdentityR40Corpus.OriginalSha);
        state.WrittenFirst = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.WriteBlobPartAsync(state.First, token));
        var chain = BlobPartIdentityR40Corpus.Next(state.Begun.Value.IntegrityHash, 0, 65536, BlobPartIdentityR40Corpus.OriginalSha);
        await SqlRf3Protocol.EqualAsync(state.Begun.Value with { NextOrdinal = 1, StoredBytes = 65536L, IntegrityHash = chain }, state.WrittenFirst.Value);
        await BlobPartIdentityR40Assertions.ReceiptAsync(state.WrittenFirst.Receipt, state.First.CommandId, state);
        state.Changed = state.First with { Bytes = BlobPartIdentityR40Corpus.Changed(), Sha256 = BlobPartIdentityR40Corpus.ChangedSha };
        await BlobPartIdentityR40Assertions.CurrentAsync(callers, state, token);
        return state;
    }

    internal static async Task FinishAsync(RequestCqrsRf3Callers callers, BlobPartIdentityR40State state, CancellationToken token)
    {
        state.Tail = new(Guid.NewGuid(), state.Begin.Blob, state.Begin.UploadId, 1,
            BlobPartIdentityR40Corpus.Bytes(65536, 4), BlobPartIdentityR40Corpus.TailSha);
        state.WrittenTail = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync(BlobPartIdentityR40Replay.PartTool, state.Tail, token))).Value;
        var chain = BlobPartIdentityR40Corpus.Next(state.WrittenFirst.Value.IntegrityHash, 1, 4, BlobPartIdentityR40Corpus.TailSha);
        await SqlRf3Protocol.EqualAsync(state.Begun.Value with { NextOrdinal = 2, StoredBytes = 65540L, IntegrityHash = chain }, state.WrittenTail.Value);
        await BlobPartIdentityR40Assertions.ReceiptAsync(state.WrittenTail.Receipt, state.Tail.CommandId, state);
        await BlobPartIdentityR40Assertions.CurrentAsync(callers, state, token);
        await BlobPartIdentityR40Replay.RunAsync(callers, state, token);
        state.Complete = new(Guid.NewGuid(), state.Begin.Blob, state.Begin.UploadId, chain);
        state.Completed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(state.Complete, token));
        await SqlRf3Protocol.EqualAsync(new BlobMetadata(state.Begin.Blob, 1L, state.Begin.UploadId, 65540L, 2,
            chain, new RowAccess(), state.Completed.Value.UpdatedAt, false), state.Completed.Value);
        await BlobPartIdentityR40Assertions.ReceiptAsync(state.Completed.Receipt, state.Complete.CommandId, state);
        await BlobPartIdentityR40Assertions.CurrentAsync(callers, state, token);
    }
}
