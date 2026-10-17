using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobSelectiveReadR43Publication
{
    internal const string SiblingResource = "selective-read-sibling";
    private const string BlobName = "selective-read-shared";
    internal static async Task<(BlobSupersessionPublicTrial.Version[], AtomicPartitionPlacementResolution)> CreateAsync(
        RequestCqrsRf3Callers admin, CancellationToken token)
    {
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(admin.Sdk, token);
        var resource = new ResourceDefinition(SiblingResource, ResourceKind.BlobStore, scenario.Partition.TransactionDomainId);
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(scenario.Partition.TenantId, scenario.Partition.DatabaseId, resource), token));
        var request = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        var first = await PublishAsync(admin, new(scenario.Partition, RelationalSqlRf3Tokens.Blobs, BlobName), 0, placement, token);
        var second = await PublishAsync(admin, new(scenario.Partition, SiblingResource, BlobName), 1, placement, token);
        return ([first, second], placement);
    }
    private static async Task<BlobSupersessionPublicTrial.Version> PublishAsync(RequestCqrsRf3Callers admin,
        BlobRef blob, int index, AtomicPartitionPlacementResolution placement, CancellationToken token)
    {
        var bytes = BlobSelectiveReadR43State.Bytes(index);
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(), bytes.Length, 0);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.BeginBlobUploadAsync(begin, token));
        var sha = BlobSelectiveReadR43Integrity.Part(index);
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, begin.UploadId, 0, bytes, sha);
        var written = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.WriteBlobPartAsync(part, token));
        var hash = BlobSelectiveReadR43Integrity.Next(begun.Value.IntegrityHash, bytes.Length, sha);
        await Assert.That(written.Value.IntegrityHash).IsEqualTo(hash);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, begin.UploadId, hash);
        var completed = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await admin.Mcp.CallAsync("keyload_blobs_complete_upload", complete, token))).Value;
        await SqlRf3Protocol.EqualAsync(new BlobMetadata(blob, 1L, begin.UploadId, bytes.Length, 1,
            hash, new RowAccess(), completed.Value.UpdatedAt), completed.Value);
        await ReceiptAsync(begun.Receipt, begin.CommandId, blob, placement);
        await ReceiptAsync(written.Receipt, part.CommandId, blob, placement);
        await ReceiptAsync(completed.Receipt, complete.CommandId, blob, placement);
        return new(begin, begun, part, written, complete, completed);
    }
    private static async Task ReceiptAsync(CommitReceipt receipt, Guid commandId, BlobRef blob,
        AtomicPartitionPlacementResolution placement)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
