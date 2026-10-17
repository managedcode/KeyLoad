using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateRelationalProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateRelationalMutations
{
    internal static async Task AddAsync(FunctionalGateContext ctx, string id, CancellationToken token)
    {
        var placement = await ctx.SdkAsync(ctx.Administrator.Sdk.ReadAtomicPartitionPlacementAsync(new(P.PlacementVersion, ctx.Partition), token));
        var commandId = Guid.NewGuid();
        var request = new SqlOperationRequest(ctx.Partition, P.Insert, FunctionalGateRelationalRows.InsertParameters(ctx, id),
            CommandId: commandId, OwnershipEpoch: placement.PlacementEpoch);
        var receipt = await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(ctx, ctx.Writer, request, token);
        await FunctionalGateRelationalAssertions.ReceiptAsync(receipt, commandId, ctx.Partition,
            [new(P.PutKind, P.Parent, id, P.Initial)]);
        await SqlRf3Protocol.EqualAsync(receipt,
            await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(ctx, ctx.Writer, request, token));
        await FunctionalGateRelationalAssertions.AnchorAsync(ctx, id, receipt.Token, token);
        var children = new CommandRequest(Guid.NewGuid(), ctx.Partition,
        [
            new PutDocument(P.Child, id, FunctionalGateRelationalRows.Child(id, null), P.Absent),
            new PutDocument(P.Independent, id, FunctionalGateRelationalRows.Independent(id), P.Absent)
        ]);
        var actual = await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.DocumentsCommit, children, token);
        await FunctionalGateRelationalAssertions.ReceiptAsync(actual, children.CommandId, ctx.Partition,
            [new(P.PutKind, P.Child, id, P.Initial), new(P.PutKind, P.Independent, id, P.Initial)]);
    }

    internal static async Task LinkAsync(FunctionalGateContext ctx, string id, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition,
        [
            new PatchDocument(P.Parent, id, [new(P.AmountPath, PatchKind.Set, P.ChangedAmount)], P.Initial),
            new PatchDocument(P.Child, id, [new(P.ParentPath, PatchKind.Set, JsonSerializer.Serialize(id))], P.Initial)
        ]);
        var actual = await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token));
        await FunctionalGateRelationalAssertions.ReceiptAsync(actual, command.CommandId, ctx.Partition,
            [new(P.PatchKindName, P.Parent, id, P.Updated), new(P.PatchKindName, P.Child, id, P.Updated)]);
    }

    internal static async Task UnlinkAsync(FunctionalGateContext ctx, string id, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition,
        [
            new PatchDocument(P.Child, id, [new(P.ParentPath, PatchKind.Set, P.NullJson)], P.Updated),
            new PatchDocument(P.Independent, id, [new(P.ActivePath, PatchKind.Set, P.FalseJson)], P.Initial)
        ]);
        var actual = await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(ctx, ctx.Writer,
            FunctionalGateRelationalCalls.Commit(command), token);
        await FunctionalGateRelationalAssertions.ReceiptAsync(actual, command.CommandId, ctx.Partition,
            [new(P.PatchKindName, P.Child, id, P.Unlinked), new(P.PatchKindName, P.Independent, id, P.Updated)]);
        var selected = await FunctionalGateRelationalCalls.SqlMcpAsync<QueryPage>(ctx, ctx.Reader,
            FunctionalGateRelationalCalls.Query(ctx, P.IndependentSelection, id), token);
        await FunctionalGateRelationalAssertions.PageAsync(selected, [new(id, P.Updated, P.FalseRow, false, [])]);
    }

    internal static async Task DeleteAsync(FunctionalGateContext ctx, string id, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition,
        [
            new DeleteDocument(P.Parent, id, P.Updated), new DeleteDocument(P.Child, id, P.Unlinked),
            new DeleteDocument(P.Independent, id, P.Updated)
        ]);
        var actual = await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(ctx, ctx.Writer,
            FunctionalGateRelationalCalls.Commit(command), token);
        await FunctionalGateRelationalAssertions.ReceiptAsync(actual, command.CommandId, ctx.Partition,
            [new(P.DeleteKind, P.Parent, id, P.Unlinked), new(P.DeleteKind, P.Child, id, P.DeletedChild),
                new(P.DeleteKind, P.Independent, id, P.Unlinked)]);
    }
}
