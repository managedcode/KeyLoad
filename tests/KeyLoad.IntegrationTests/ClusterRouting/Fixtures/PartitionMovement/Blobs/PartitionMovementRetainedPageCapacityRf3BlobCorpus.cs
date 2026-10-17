using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Completed literal native parts are real SDK effects; saved receipts remain exact across movement.</summary>
internal sealed class PartitionMovementRetainedPageCapacityRf3BlobCorpus
{
    private const int FirstOrdinal = 0;
    private const long InitialRevision = 0;
    private const int LargePartCount = 8;
    private const int TailLength = 4;
    private const byte FirstPartByte = 0x61;
    private const byte TailByte = 0x7f;
    private const string BlobId = "parent-retained-page-capacity";
    private const string ReadTool = "keyload_blobs_read_range";
    private const string BeginTool = "keyload_blobs_begin_upload";
    private const string WriteTool = "keyload_blobs_write_part";
    private const string CompleteTool = "keyload_blobs_complete_upload";
    private readonly List<(WriteBlobPartRequest Request, BlobCommitResult<BlobUploadInfo> Result)> parts = [];
    private readonly BeginBlobUploadRequest begin;
    private readonly BlobCommitResult<BlobUploadInfo> begun;
    private CompleteBlobUploadRequest complete = null!;
    private BlobCommitResult<BlobMetadata> completed = null!;

    private PartitionMovementRetainedPageCapacityRf3BlobCorpus(BeginBlobUploadRequest begin,
        BlobCommitResult<BlobUploadInfo> begun)
    {
        this.begin = begin;
        this.begun = begun;
    }

    internal static async Task<PartitionMovementRetainedPageCapacityRf3BlobCorpus> CreateAsync(
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var blob = new BlobRef(seed.Partition, RelationalSqlRf3Tokens.Blobs, BlobId);
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(),
            checked((long)LargePartCount * BlobLimits.RawPartBytes + TailLength), InitialRevision);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.BeginBlobUploadAsync(begin,
            cancellationToken).ConfigureAwait(false));
        await RequireReceiptAsync(seed, begun.Receipt, begin.CommandId);
        var corpus = new PartitionMovementRetainedPageCapacityRf3BlobCorpus(begin, begun);
        var hash = begun.Value.IntegrityHash;
        for (var ordinal = FirstOrdinal; ordinal <= LargePartCount; ordinal++)
        {
            var bytes = Literal(ordinal);
            var request = new WriteBlobPartRequest(Guid.NewGuid(), blob, begin.UploadId, ordinal,
                bytes, BlobIntegrity.PartHash(bytes));
            var result = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.WriteBlobPartAsync(request,
                cancellationToken).ConfigureAwait(false));
            await RequireReceiptAsync(seed, result.Receipt, request.CommandId);
            corpus.parts.Add((request, result));
            hash = BlobIntegrity.NextHash(hash, ordinal, bytes.Length, request.Sha256);
        }
        corpus.complete = new(Guid.NewGuid(), blob, begin.UploadId, hash);
        corpus.completed = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CompleteBlobUploadAsync(
            corpus.complete, cancellationToken).ConfigureAwait(false));
        await RequireReceiptAsync(seed, corpus.completed.Receipt, corpus.complete.CommandId);
        await Assert.That(corpus.completed.Value.Length).IsEqualTo(begin.Length);
        await corpus.RequireAsync(seed, cancellationToken).ConfigureAwait(false);
        return corpus;
    }

    internal async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        await RequireReceiptsAsync(seed, cancellationToken).ConfigureAwait(false);
        foreach (var (request, _) in parts)
        {
            var range = new BlobReadRequest(completed.Value.Blob, completed.Value.Revision,
                checked((long)request.Ordinal * BlobLimits.RawPartBytes), request.Bytes.Length);
            var expected = new BlobReadResult(completed.Value, range.Offset, Literal(request.Ordinal));
            var actual = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadBlobRangeAsync(range,
                cancellationToken).ConfigureAwait(false));
            await SqlRf3Protocol.EqualAsync(expected, actual);
            var official = await McpCallerAssertions.SuccessAsync<BlobReadResult>(await seed.Official.CallAsync(
                ReadTool, range, cancellationToken).ConfigureAwait(false));
            await SqlRf3Protocol.EqualAsync(expected, official.Value);
            var sql = SqlRf3Protocol.Call(seed.Partition, ReadTool, range);
            await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<BlobReadResult>(seed.Source,
                sql, cancellationToken).ConfigureAwait(false));
            await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<BlobReadResult>(seed.Official,
                sql, cancellationToken).ConfigureAwait(false));
        }
    }

    private async Task RequireReceiptsAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        await SqlRf3Protocol.EqualAsync(begun, await McpCallerAssertions.SdkSuccessAsync(await seed.Source
            .BeginBlobUploadAsync(begin, cancellationToken).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await seed.Official.CallAsync(BeginTool, begin, cancellationToken).ConfigureAwait(false))).Value);
        foreach (var (request, receipt) in parts)
        {
            await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await seed.Source
                .WriteBlobPartAsync(request, cancellationToken).ConfigureAwait(false)));
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await seed.Official.CallAsync(WriteTool, request, cancellationToken).ConfigureAwait(false))).Value);
        }
        await SqlRf3Protocol.EqualAsync(completed, await McpCallerAssertions.SdkSuccessAsync(await seed.Source
            .CompleteBlobUploadAsync(complete, cancellationToken).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await seed.Official.CallAsync(CompleteTool, complete, cancellationToken).ConfigureAwait(false))).Value);
    }

    private static async Task RequireReceiptAsync(PartitionMovementPublicParentRf3Seed seed,
        CommitReceipt receipt, Guid commandId)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(seed.OriginalPlacement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(seed.OriginalPlacement.PlacementEpoch);
    }

    private static byte[] Literal(int ordinal)
    {
        var bytes = new byte[ordinal == LargePartCount ? TailLength : BlobLimits.RawPartBytes];
        Array.Fill(bytes, ordinal == LargePartCount ? TailByte : checked((byte)(FirstPartByte + ordinal)));
        return bytes;
    }
}
