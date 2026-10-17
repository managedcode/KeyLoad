using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveReadCancellationR49Current
{
    internal static QueryRow Row(long revision, string json)
        => new(LiveReadCancellationR49Protocol.Document, revision, json, false, []);

    internal static async Task RunAsync(LiveReadCancellationR49Owner owner, CancellationToken token)
    {
        var placement = new AtomicPartitionPlacementReadRequest(1, owner.Identity.Partition);
        await SqlRf3Protocol.EqualAsync(owner.Placement, await McpCallerAssertions.SdkSuccessAsync(
            await owner.Administrator.Sdk.ReadAtomicPartitionPlacementAsync(placement, token)));
        await SqlRf3Protocol.EqualAsync(owner.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await owner.Administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placement, token))).Value);
        var expectedDocument = new DocumentReadResult(new(owner.Reference, owner.Revision, owner.Json, false, []));
        await SqlRf3Protocol.EqualAsync(expectedDocument, await McpCallerAssertions.SdkSuccessAsync(
            await owner.Caller.Sdk.GetAsync(owner.Reference, owner.CurrentReceipt.Token, token)));
        await SqlRf3Protocol.EqualAsync(expectedDocument, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
            await owner.Caller.Mcp.CallAsync("keyload_documents_get", new GetDocumentRequest(owner.Reference, owner.CurrentReceipt.Token), token))).Value);
        await QueryAsync(await McpCallerAssertions.SdkSuccessAsync(await owner.Caller.Sdk.QueryAstAsync(owner.Query, token)), owner);
        await QueryAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
            await owner.Caller.Mcp.CallAsync(LiveQueryCutRf3Assertions.QueryTool, owner.Query, token))).Value, owner);
        await SqlRf3Protocol.EqualAsync(owner.Outbox, await McpCallerAssertions.SdkSuccessAsync(
            await owner.Administrator.Sdk.OutboxStatusAsync(owner.Identity.Partition, token)));
        await SqlRf3Protocol.EqualAsync(owner.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await owner.Administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(owner.Identity.Partition), token))).Value);
    }

    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid id, long revision, LiveReadCancellationR49Owner owner)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(owner.Identity.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(owner.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(owner.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(new[] { new MutationReceipt(LiveReadCancellationR49Protocol.PutDocument, LiveReadCancellationR49Protocol.Collection,
            LiveReadCancellationR49Protocol.Document, revision) }, receipt.Mutations.ToArray());
    }

    private static async Task QueryAsync(QueryPage page, LiveReadCancellationR49Owner owner)
    {
        await SqlRf3Protocol.EqualAsync(new[] { Row(owner.Revision, owner.Json) }, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(LiveReadCancellationR49Protocol.FullScan);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
    }
}
