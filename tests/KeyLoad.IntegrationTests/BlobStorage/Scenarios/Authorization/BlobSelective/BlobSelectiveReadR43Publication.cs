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
    internal static async Task<BlobSupersessionPublicTrial.Version[]> CreateAsync(
        RequestCqrsClusterCallers admin, CancellationToken token)
    {
        var scenario = await RelationalSqlClusterScenario.CreateAsync(admin.Sdk, token);
        var resource = new ResourceDefinition(SiblingResource, ResourceKind.BlobStore, scenario.Partition.TransactionDomainId);
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(scenario.Partition.TenantId, scenario.Partition.DatabaseId, resource), token));
        var first = await PublishAsync(admin, new(scenario.Partition, RelationalSqlClusterTokens.Blobs, BlobName), 0, token);
        var second = await PublishAsync(admin, new(scenario.Partition, SiblingResource, BlobName), 1, token);
        return [first, second];
    }
    private static async Task<BlobSupersessionPublicTrial.Version> PublishAsync(RequestCqrsClusterCallers admin,
        BlobRef blob, int index, CancellationToken token)
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
        await SqlClusterProtocol.EqualAsync(new BlobMetadata(blob, 1L, begin.UploadId, bytes.Length, 1,
            hash, new RowAccess(), completed.Value.UpdatedAt), completed.Value);
        await ReceiptAsync(begun.Receipt, begin.CommandId, blob);
        await ReceiptAsync(written.Receipt, part.CommandId, blob);
        await ReceiptAsync(completed.Receipt, complete.CommandId, blob);
        return new(begin, begun, part, written, complete, completed);
    }
    private static async Task ReceiptAsync(CommitReceipt receipt, Guid commandId, BlobRef blob)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
