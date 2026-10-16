using System.Security.Cryptography;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryModelsBlob
{
    private const int PartCount = 2;

    internal static async Task<FunctionalGateColdBlob> PublishAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership,
        FunctionalGateContext context, List<ResourcePartitionInventoryRf3Creation> creations, CancellationToken token)
    {
        var definition = ResourcePartitionInventoryModelsProtocol.Definition("blobs", ResourceKind.BlobStore);
        var creation = new ResourcePartitionInventoryRf3Creation(Guid.NewGuid(),
            new(context.Partition.TenantId, context.Partition.DatabaseId, definition));
        await SqlRf3Protocol.EqualAsync(definition, await context.SdkAsync(context.Administrator.Sdk.ConfigureResourceAsync(
            creation.CommandId, creation.Request, token)));
        creations.Add(creation);
        var before = await ResourcePartitionInventoryModelsInventoryAssertions.ReadAsync(
            wave, seed, membership, definition, populated: false, token);
        var blob = ResourcePartitionInventoryModelsProtocol.Blob;
        var bytes = FunctionalGateBlobUpload.Content(context, blob, ResourcePartitionInventoryModelsProtocol.Index);
        var placement = await context.SdkAsync(context.Administrator.Sdk.ReadAtomicPartitionPlacementAsync(
            new(ResourcePartitionInventoryModelsProtocol.Version, context.Partition), token));
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(), bytes.Length,
            ResourcePartitionInventoryModelsProtocol.MissingRevision, new RowAccess());
        var begun = await context.SdkAsync(context.Writer.Sdk.BeginBlobUploadAsync(begin, token));
        await ReceiptAsync(begun.Receipt, begin.CommandId, blob, placement);
        var current = new BlobUploadInfo(blob, begin.UploadId, bytes.Length, 0, 0, 0, begun.Value.ExpiresAt,
            BlobUploadStatus.Active, BlobIntegrity.InitialHash(begun.Receipt.Token.Incarnation, blob, begin.UploadId, bytes.Length));
        await SqlRf3Protocol.EqualAsync(current, begun.Value);
        var first = await ResourcePartitionInventoryModelsInventoryAssertions.ReadAsync(
            wave, seed, membership, definition, populated: true, token);
        await ResourcePartitionInventoryModelsInventoryAssertions.FirstEffectAsync(before, first);
        var parts = new List<FunctionalGateColdPart>();
        for (var ordinal = 0; ordinal < PartCount; ordinal++)
        {
            var offset = ordinal * BlobLimits.RawPartBytes;
            var part = bytes.AsMemory(offset, Math.Min(BlobLimits.RawPartBytes, bytes.Length - offset));
            var hash = Convert.ToHexStringLower(SHA256.HashData(part.Span));
            var request = new WriteBlobPartRequest(Guid.NewGuid(), blob, begin.UploadId, ordinal, part, hash);
            var result = await context.McpAsync<BlobCommitResult<BlobUploadInfo>>(
                context.Writer, "keyload_blobs_write_part", request, token);
            await ReceiptAsync(result.Receipt, request.CommandId, blob, placement);
            current = current with
            {
                NextOrdinal = ordinal + 1,
                StoredBytes = offset + part.Length,
                IntegrityHash = BlobIntegrity.NextHash(current.IntegrityHash, ordinal, part.Length, hash)
            };
            await SqlRf3Protocol.EqualAsync(current, result.Value);
            parts.Add(new(request, result));
        }
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, begin.UploadId, current.IntegrityHash);
        var published = await context.SdkAsync(context.Writer.Sdk.CompleteBlobUploadAsync(complete, token));
        await ReceiptAsync(published.Receipt, complete.CommandId, blob, placement);
        await SqlRf3Protocol.EqualAsync(new BlobMetadata(blob, ResourcePartitionInventoryModelsProtocol.InitialRevision,
            begin.UploadId, bytes.Length, PartCount, current.IntegrityHash, new RowAccess(), published.Value.UpdatedAt), published.Value);
        return new(begin, begun, [.. parts], complete, published, bytes);
    }

    private static async Task ReceiptAsync(CommitReceipt receipt, Guid commandId, BlobRef blob,
        AtomicPartitionPlacementResolution placement)
    {
        await Assert.That(receipt.Token.Position).IsGreaterThan(ResourcePartitionInventoryModelsProtocol.MissingRevision);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(commandId,
            new(placement.Incarnation, blob.Partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
            [], DurabilityProfile.QuorumProcessDurable), receipt);
    }
}
