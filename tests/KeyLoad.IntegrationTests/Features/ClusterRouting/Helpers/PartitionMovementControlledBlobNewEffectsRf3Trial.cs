using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementControlledBlobNewEffectsRf3Trial
{
    private const string BlobId = "parent-controlled-postmove";
    private const int ExpectedInitialRevision = 0;
    private const int FirstOrdinal = 0;
    private const int TailOrdinal = 1;
    private const byte FirstByte = 0x43;
    private const byte TailByte = 0x44;

    internal static async Task<PartitionMovementPublicParentRf3BlobOriginals> RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveResult terminal, CancellationToken token)
    {
        var blob = new BlobRef(seed.Partition, seed.Blob.Blob.Resource, BlobId);
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload,
            seed.BlobOriginals.Begin.Length, ExpectedInitialRevision);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.BeginBlobUploadAsync(begin, token).ConfigureAwait(false));
        await RequireReceiptAsync(seed, terminal, begin.CommandId, begun.Receipt);
        var firstBytes = new byte[BlobLimits.RawPartBytes];
        Array.Fill(firstBytes, FirstByte);
        var first = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, FirstOrdinal, firstBytes, BlobIntegrity.PartHash(firstBytes));
        var failedPart = first with { CommandId = Guid.NewGuid(), Ordinal = TailOrdinal };
        await RequireFailedPartAsync(seed, failedPart, begun.Value, token).ConfigureAwait(false);
        var firstReceipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.WriteBlobPartAsync(first, token).ConfigureAwait(false));
        await RequireReceiptAsync(seed, terminal, first.CommandId, firstReceipt.Receipt);
        var tailBytes = new byte[seed.BlobOriginals.Tail.Bytes.Length];
        Array.Fill(tailBytes, TailByte);
        var tail = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, TailOrdinal, tailBytes, BlobIntegrity.PartHash(tailBytes));
        var tailReceipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.WriteBlobPartAsync(tail, token).ConfigureAwait(false));
        await RequireReceiptAsync(seed, terminal, tail.CommandId, tailReceipt.Receipt);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, tailReceipt.Value.IntegrityHash);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CompleteBlobUploadAsync(complete, token).ConfigureAwait(false));
        await RequireReceiptAsync(seed, terminal, complete.CommandId, completed.Receipt);
        await RequireReplayAsync(seed, BlobToolNames.BeginUpload, begin, begun, token).ConfigureAwait(false);
        await RequireReplayAsync(seed, BlobToolNames.WritePart, first, firstReceipt, token).ConfigureAwait(false);
        await RequireReplayAsync(seed, BlobToolNames.WritePart, tail, tailReceipt, token).ConfigureAwait(false);
        await RequireReplayAsync(seed, BlobToolNames.CompleteUpload, complete, completed, token).ConfigureAwait(false);
        await RequireBytesAsync(seed, completed.Value, FirstOrdinal, firstBytes, token).ConfigureAwait(false);
        await RequireBytesAsync(seed, completed.Value, BlobLimits.RawPartBytes, tailBytes, token).ConfigureAwait(false);
        return new(begin, begun, first, firstReceipt, tail, tailReceipt, complete, completed, failedPart);
    }

    internal static async Task RequireColdReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3BlobOriginals originals, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(originals.Begun, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.BeginBlobUploadAsync(originals.Begin, token).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(originals.FirstReceipt, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.WriteBlobPartAsync(originals.First, token).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(originals.TailReceipt, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.WriteBlobPartAsync(originals.Tail, token).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(originals.Completed, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.CompleteBlobUploadAsync(originals.Complete, token).ConfigureAwait(false)));
        await RequireReplayAsync(seed, BlobToolNames.BeginUpload, originals.Begin, originals.Begun, token).ConfigureAwait(false);
        await RequireReplayAsync(seed, BlobToolNames.WritePart, originals.First, originals.FirstReceipt, token).ConfigureAwait(false);
        await RequireReplayAsync(seed, BlobToolNames.WritePart, originals.Tail, originals.TailReceipt, token).ConfigureAwait(false);
        await RequireReplayAsync(seed, BlobToolNames.CompleteUpload, originals.Complete, originals.Completed, token).ConfigureAwait(false);
        await RequireCurrentBytesAsync(seed, originals, token).ConfigureAwait(false);
    }

    internal static async Task RequireCurrentBytesAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3BlobOriginals originals, CancellationToken token)
    {
        var firstBytes = new byte[BlobLimits.RawPartBytes];
        Array.Fill(firstBytes, FirstByte);
        await RequireBytesAsync(seed, originals.Completed.Value, FirstOrdinal, firstBytes, token).ConfigureAwait(false);
        var tailBytes = new byte[originals.Tail.Bytes.Length];
        Array.Fill(tailBytes, TailByte);
        await RequireBytesAsync(seed, originals.Completed.Value, BlobLimits.RawPartBytes, tailBytes, token).ConfigureAwait(false);
    }

    private static async Task RequireFailedPartAsync(PartitionMovementPublicParentRf3Seed seed,
        WriteBlobPartRequest request, BlobUploadInfo expected, CancellationToken token)
    {
        var failed = await seed.Source.WriteBlobPartAsync(request, token).ConfigureAwait(false);
        await Assert.That(failed.IsFailed).IsTrue();
        await Assert.That(failed.Value).IsNull();
        await Assert.That(failed.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(BlobToolNames.WritePart, request, token)
            .ConfigureAwait(false), ErrorCode.Validation, dispatched: true);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.GetBlobUploadInfoAsync(
            new(request.Blob, request.UploadId), token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }

    private static async Task RequireReceiptAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveResult terminal, Guid commandId, CommitReceipt receipt)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(terminal.DestinationOwner.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(terminal.PublishedPlacement!.PlacementEpoch);
    }

    private static async Task RequireReplayAsync<TRequest, TResult>(PartitionMovementPublicParentRf3Seed seed,
        string tool, TRequest request, TResult expected, CancellationToken token)
    {
        var official = (await McpCallerAssertions.SuccessAsync<TResult>(await seed.Official.CallAsync(tool, request, token)
            .ConfigureAwait(false))).Value;
        await SqlRf3Protocol.EqualAsync(expected, official);
        var sql = SqlRf3Protocol.Call(seed.Partition, tool, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<TResult>(seed.Source, sql, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<TResult>(seed.Official, sql, token).ConfigureAwait(false));
    }

    private static async Task RequireBytesAsync(PartitionMovementPublicParentRf3Seed seed,
        BlobMetadata metadata, long offset, byte[] bytes, CancellationToken token)
    {
        var request = new BlobReadRequest(metadata.Blob, metadata.Revision, offset, bytes.Length);
        var expected = new BlobReadResult(metadata, offset, bytes);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadBlobRangeAsync(request, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(expected, sdk);
        await RequireReplayAsync(seed, BlobToolNames.ReadRange, request, expected, token).ConfigureAwait(false);
    }
}
