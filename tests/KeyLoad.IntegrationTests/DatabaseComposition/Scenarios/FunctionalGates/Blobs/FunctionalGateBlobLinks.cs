using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateBlobLinks
{
    internal static async Task<EntityRef> LinkAsync(FunctionalGateContext context,
        FunctionalGateBlobVersion published, int iteration, CancellationToken token)
    {
        var reference = context.Bind(new EntityRef(context.Partition, context.MutableDocumentCollection, $"functional_blob_{iteration}"));
        var json = LinkJson(context, reference, published.Begin.Blob);
        var request = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new PutDocument(reference.Collection, reference.Id, json, 0L, new RowAccess())]);
        var receipt = await context.SdkAsync(context.Writer.Sdk.CommitAsync(request, token));
        await ReceiptAsync(context, receipt, request, reference, "putDocument", 1L);
        reference = await FunctionalGateEntityReferences.CaptureAsync(context, context.Reader,
            new DocumentResult(reference, 1L, json, false, []), token);
        var expected = new DocumentResult(reference, 1L, json, false, []);
        await SqlClusterProtocol.EqualAsync(expected,
            (await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, receipt.Token, token))).Document);
        await SqlClusterProtocol.EqualAsync(expected, (await context.McpAsync<DocumentReadResult>(
            context.Reader, McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, receipt.Token), token)).Document);
        await SqlClusterProtocol.EqualAsync(receipt, await context.McpAsync<CommitReceipt>(
            context.Writer, McpCallerTools.DocumentsCommit, request, token));
        await SqlClusterProtocol.EqualAsync(receipt, await context.SdkAsync(context.Writer.Sdk.CommitAsync(request, token)));
        return reference;
    }

    internal static async Task UnlinkAsync(FunctionalGateContext context, EntityRef reference, CancellationToken token)
    {
        var json = LinkJson(context, reference, null);
        var request = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new PutDocument(reference.Collection, reference.Id, json, 1L, new RowAccess())]);
        var receipt = await context.McpAsync<CommitReceipt>(context.Writer, McpCallerTools.DocumentsCommit, request, token);
        await ReceiptAsync(context, receipt, request, reference, "putDocument", 2L);
        var expected = new DocumentResult(reference, 2L, json, false, []);
        await SqlClusterProtocol.EqualAsync(expected,
            (await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, receipt.Token, token))).Document);
        await SqlClusterProtocol.EqualAsync(expected, (await context.McpAsync<DocumentReadResult>(
            context.Reader, McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, receipt.Token), token)).Document);
        await SqlClusterProtocol.EqualAsync(receipt, await context.SdkAsync(context.Writer.Sdk.CommitAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(receipt,
            await context.McpAsync<CommitReceipt>(context.Writer, McpCallerTools.DocumentsCommit, request, token));
        await RemoveAsync(context, reference, token);
    }

    internal static async Task BaselineAsync(FunctionalGateContext context,
        BlobRef blob, int index, CancellationToken token)
    {
        var reference = context.Bind(new EntityRef(context.Partition, context.MutableDocumentCollection, $"functional_blob_{-1 - index}"));
        var expected = new DocumentResult(reference, 1L, LinkJson(context, reference, blob), false, []);
        await SqlClusterProtocol.EqualAsync(expected,
            (await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, token))).Document);
        await SqlClusterProtocol.EqualAsync(expected, (await context.McpAsync<DocumentReadResult>(
            context.Reader, McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token)).Document);
    }

    private static string LinkJson(FunctionalGateContext context, EntityRef reference, BlobRef? blob)
        => JsonSerializer.Serialize(new
        {
            id = reference.Id,
            anchor = context.Bind(context.Anchor),
            blobRef = blob
        }, JsonDefaults.Options);

    private static async Task RemoveAsync(FunctionalGateContext context, EntityRef reference, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new DeleteDocument(reference.Collection, reference.Id, 2L)]);
        var receipt = await context.SdkAsync(context.Writer.Sdk.CommitAsync(request, token));
        await ReceiptAsync(context, receipt, request, reference, "deleteDocument", 3L);
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(null,
            (await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, receipt.Token, token))).Document);
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(null, (await context.McpAsync<DocumentReadResult>(
            context.Reader, McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, receipt.Token), token)).Document);
        await SqlClusterProtocol.EqualAsync(receipt, await context.McpAsync<CommitReceipt>(
            context.Writer, McpCallerTools.DocumentsCommit, request, token));
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(null,
            (await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, receipt.Token, token))).Document);
    }

    private static async Task ReceiptAsync(FunctionalGateContext context, CommitReceipt receipt, CommandRequest request,
        EntityRef reference, string kind, long revision)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(reference.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(context.ReplicaCount);
        await SqlClusterProtocol.EqualAsync<System.Collections.Immutable.ImmutableArray<MutationReceipt>>(
            [new(kind, reference.Collection, reference.Id, revision)], receipt.Mutations);
    }
}
