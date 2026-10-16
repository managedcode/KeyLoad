using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateQueueGraphProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateQueueGraph
{
    internal static async Task RunAsync(FunctionalGateContext ctx, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(P.GuidFormat);
        var from = new EntityRef(ctx.Partition, ctx.MutableDocumentCollection, P.FromPrefix + suffix);
        var to = new EntityRef(ctx.Partition, ctx.MutableDocumentCollection, P.ToPrefix + suffix);
        var link = new QueueGraphLink(from, to, P.Label, P.Attributes);
        var sourceId = P.SourcePrefix + suffix;
        var edgePrefix = P.EdgePrefix + suffix + P.Separator;
        var returnPrefix = P.ReturnPrefix + suffix + P.Separator;
        var edgeId = edgePrefix + sourceId;
        var returnedId = returnPrefix + edgeId;
        var source = new QueueLaneRef(ctx.Partition, FunctionalGateQueues.Queue(P.SourceQueue));
        var target = new QueueLaneRef(ctx.Partition, FunctionalGateQueues.Queue(P.TargetQueue));
        var payload = DatabaseCompositionRf3Payload.OrdinalGolden(link);
        await SeedAsync(ctx, link, source, sourceId, payload, token);
        var original = await FunctionalGateQueueGraphQueue.ReadyAsync(ctx, source, sourceId, payload, token);
        await ForwardAsync(ctx, source, edgePrefix, edgeId, token);
        await FunctionalGateQueueGraphAssertions.EdgeAsync(ctx, link, edgeId, present: true, token);
        await FunctionalGateQueues.EqualAsync(original,
            await FunctionalGateQueueGraphQueue.ReadyAsync(ctx, source, sourceId, payload, token));
        await FunctionalGateQueueGraphQueue.ConsumeAsync(ctx, source, original, token);
        await ReverseAsync(ctx, target, link, returnPrefix, returnedId, token);
        var returned = await FunctionalGateQueueGraphQueue.ReadyAsync(ctx, target, returnedId, payload, token);
        await FunctionalGateQueueGraphQueue.ConsumeAsync(ctx, target, returned, token);
        await CleanupAsync(ctx, link, edgeId, token);
    }

    private static async Task SeedAsync(FunctionalGateContext ctx, QueueGraphLink link,
        QueueLaneRef source, string id, string payload, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition,
        [
            new PutDocument(link.From.Collection, link.From.Id, P.DocumentJson, P.Absent),
            new PutDocument(link.To.Collection, link.To.Id, P.DocumentJson, P.Absent),
            new EnqueueMessage(source.Queue, id, payload)
        ]);
        var receipt = await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token));
        await FunctionalGateCompositionCalls.ReceiptAsync(ctx, receipt, command.CommandId,
        [
            new(P.PutKind, link.From.Collection, link.From.Id, P.Created),
            new(P.PutKind, link.To.Collection, link.To.Id, P.Created),
            new(P.EnqueueKind, source.Queue, id, P.Created)
        ], token);
        await FunctionalGateQueueGraphAssertions.DocumentAsync(ctx, link.From, present: true, token);
        await FunctionalGateQueueGraphAssertions.DocumentAsync(ctx, link.To, present: true, token);
    }

    private static async Task ForwardAsync(FunctionalGateContext ctx, QueueLaneRef source,
        string prefix, string edgeId, CancellationToken token)
    {
        var mutation = new QueueToGraph(P.Graph, source.Queue, prefix, P.MaximumMessages);
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition, [mutation]);
        var sql = DeclarativeCompositionSql.Forward(command, mutation) with
        {
            CommandId = command.CommandId
        };
        var actual = await FunctionalGateCompositionCalls.SqlAsync<CommitReceipt>(ctx, sql, false, true, token);
        var replay = await FunctionalGateCompositionCalls.SqlAsync<CommitReceipt>(ctx, sql, true, true, token);
        await FunctionalGateCompositionCalls.ReceiptAsync(ctx, actual, command.CommandId,
            [new(P.EdgeKind, P.Graph, edgeId, P.Created)], token);
        await FunctionalGateQueues.EqualAsync(actual, replay);
    }

    private static async Task ReverseAsync(FunctionalGateContext ctx, QueueLaneRef target,
        QueueGraphLink link, string prefix, string id, CancellationToken token)
    {
        var mutation = new GraphToQueueMutation(target.Queue, P.Graph, link.From, prefix,
            P.Depth, P.MaximumVertices, P.MaximumEdges);
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition, [mutation]);
        var sql = DeclarativeCompositionSql.Reverse(command, mutation) with
        {
            CommandId = command.CommandId
        };
        var actual = await FunctionalGateCompositionCalls.SqlAsync<CommitReceipt>(ctx, sql, true, true, token);
        var replay = await FunctionalGateCompositionCalls.SqlAsync<CommitReceipt>(ctx, sql, false, true, token);
        await FunctionalGateCompositionCalls.ReceiptAsync(ctx, actual, command.CommandId,
            [new(P.EnqueueKind, target.Queue, id, P.Created)], token);
        await FunctionalGateQueues.EqualAsync(actual, replay);
    }

    private static async Task CleanupAsync(FunctionalGateContext ctx, QueueGraphLink link,
        string edgeId, CancellationToken token)
    {
        var unlink = new CommandRequest(Guid.NewGuid(), ctx.Partition, [new DeleteEdge(P.Graph, edgeId, P.Created)]);
        await FunctionalGateCompositionCalls.ReceiptAsync(ctx,
            await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(unlink, token)), unlink.CommandId,
            [new(P.UnlinkKind, P.Graph, edgeId, P.Deleted)], token);
        await FunctionalGateQueueGraphAssertions.EdgeAsync(ctx, link, edgeId, present: false, token);
        var cleanup = new CommandRequest(Guid.NewGuid(), ctx.Partition,
        [
            new DeleteDocument(link.From.Collection, link.From.Id, P.Created),
            new DeleteDocument(link.To.Collection, link.To.Id, P.Created)
        ]);
        await FunctionalGateCompositionCalls.ReceiptAsync(ctx,
            await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(cleanup, token)), cleanup.CommandId,
        [
            new(P.DeleteKind, link.From.Collection, link.From.Id, P.Deleted),
            new(P.DeleteKind, link.To.Collection, link.To.Id, P.Deleted)
        ], token);
        await FunctionalGateQueueGraphAssertions.DocumentAsync(ctx, link.From, present: false, token);
        await FunctionalGateQueueGraphAssertions.DocumentAsync(ctx, link.To, present: false, token);
    }
}
