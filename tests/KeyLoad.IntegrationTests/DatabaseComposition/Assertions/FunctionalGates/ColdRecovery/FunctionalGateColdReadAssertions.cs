using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdReadAssertions
{
    internal static Task VerifyAsync(FunctionalGateContext context, FunctionalGateColdModel model, CancellationToken token)
        => VerifyRecreatedAsync(context, model, () => VerifyOriginalTableAsync(context, model, token), token);

    internal static async Task VerifyRecreatedAsync(FunctionalGateContext context, FunctionalGateColdModel model,
        Func<Task> verifyFreshTable, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(verifyFreshTable);
        var index = model.Index;
        var reference = context.Bind(new(context.Partition, FunctionalGateColdProtocol.Resource("docs", index), FunctionalGateColdProtocol.Id));
        var expected = new DocumentReadResult(new(reference, 1,
            FunctionalGateColdProtocol.DocumentJson(context.Bind(context.Anchor), model.Blob.Begin.Blob), false, []));
        await SqlRf3Protocol.EqualAsync(expected, await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, token)));
        await SqlRf3Protocol.EqualAsync(expected, await context.McpAsync<DocumentReadResult>(context.Reader,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
        var anchor = new DocumentReadResult(new(context.Bind(context.Anchor), 1, "{\"purpose\":\"cold-anchor\"}", false, []));
        await SqlRf3Protocol.EqualAsync(anchor, await context.SdkAsync(context.Reader.Sdk.GetAsync(context.Bind(context.Anchor), token)));
        await SqlRf3Protocol.EqualAsync(anchor, await context.McpAsync<DocumentReadResult>(context.Reader,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(context.Bind(context.Anchor)), token));
        await verifyFreshTable();
        var edge = new EdgeRecord(FunctionalGateColdProtocol.Edge, reference, context.Bind(context.Anchor), "retained", "{}", 1);
        var traversal = new global::KeyLoad.GraphTraversal([.. new[] { reference, context.Bind(context.Anchor) }
            .OrderBy(item => item.Collection, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal)], [edge]);
        var graph = new TraverseRequest(context.Partition, FunctionalGateColdProtocol.Resource("graph", index), reference, 1, 2, 1);
        await SqlRf3Protocol.EqualAsync(traversal, await context.SdkAsync(context.Reader.Sdk.TraverseAsync(graph, token)));
        await SqlRf3Protocol.EqualAsync(traversal, await context.McpAsync<global::KeyLoad.GraphTraversal>(context.Reader, McpCallerTools.GraphTraverse, graph, token));
        var raw = new ReadSamplesRequest(context.Partition, FunctionalGateColdProtocol.Resource("series", index),
            FunctionalGateColdProtocol.Series, FunctionalGateColdProtocol.Start, FunctionalGateColdProtocol.Start.AddMinutes(2), 2);
        var samples = FunctionalGateColdProtocol.Samples.Select((sample, ordinal) =>
            new SampleRecord(FunctionalGateColdProtocol.Series, sample, ordinal + 1, FunctionalGateColdProtocol.Tags(context.Bind(context.Anchor)))).ToArray();
        await SqlRf3Protocol.EqualAsync(samples, await context.SdkAsync(context.Reader.Sdk.ReadSamplesAsync(raw, token)));
        await SqlRf3Protocol.EqualAsync(samples, await context.McpAsync<SampleRecord[]>(context.Reader, McpCallerTools.SeriesRead, raw, token));
        var aggregate = new AggregateSamplesRequest(context.Partition, raw.Set, raw.SeriesId, raw.From, raw.Until, 2);
        await SqlRf3Protocol.EqualAsync(new SampleAggregate(2, 6, 2, 4, 3), await context.SdkAsync(context.Reader.Sdk.AggregateSamplesAsync(aggregate, token)));
        await SqlRf3Protocol.EqualAsync(new SampleAggregate(2, 6, 2, 4, 3), await context.McpAsync<SampleAggregate>(context.Reader, McpCallerTools.SeriesAggregate, aggregate, token));
        await FunctionalGateBlobAssertions.ContentAsync(context, model.Blob.Published.Value, model.Blob.Bytes, token);
        await FunctionalGateColdHistory.VerifyAsync(context, model, token);
        await FunctionalGateColdSearch.VerifyAsync(context, model, token);
    }

    private static async Task VerifyOriginalTableAsync(FunctionalGateContext context, FunctionalGateColdModel model,
        CancellationToken token)
    {
        var index = model.Index;
        var reference = context.Bind(new(context.Partition, FunctionalGateColdProtocol.Resource("docs", index), FunctionalGateColdProtocol.Id));
        var table = FunctionalGateColdProtocol.Select(context.Partition, index);
        var json = JsonSerializer.Serialize(new
        {
            value = 37,
            reference = JsonSerializer.Serialize(reference, JsonDefaults.Options)
        }, JsonDefaults.Options);
        await FunctionalGateRelationalAssertions.PageAsync(await FunctionalGateRelationalCalls.SqlSdkAsync<QueryPage>(context, context.Reader, table, token),
            [new(FunctionalGateColdProtocol.Id, 1, json, false, [])]);
        await FunctionalGateRelationalAssertions.PageAsync(await FunctionalGateRelationalCalls.SqlMcpAsync<QueryPage>(context, context.Reader, table, token),
            [new(FunctionalGateColdProtocol.Id, 1, json, false, [])]);
    }

    internal static async Task InitialHistoryAsync(FunctionalGateContext context, int index, EntityRef reference,
        StreamPage stream, EventSourcePage topic, MessageInspectionResult message, long minimum, DateTimeOffset beforeWrite)
    {
        reference = context.Bind(reference);
        var expectedStream = new StreamRef(context.Partition, FunctionalGateColdProtocol.Resource("events", index), FunctionalGateColdProtocol.Id);
        await SqlRf3Protocol.EqualAsync(expectedStream, stream.Stream);
        await SqlRf3Protocol.EqualAsync(new StreamHead(1, 1, 1), stream.Head);
        var item = await Assert.That(stream.Events).HasSingleItem();
        await SqlRf3Protocol.EqualAsync(expectedStream, item.Stream);
        await Assert.That(item.Revision).IsEqualTo(1L);
        await SqlRf3Protocol.EqualAsync(FunctionalGateColdProtocol.Data(reference), item.Data);
        await Assert.That(item.EventSequence).IsGreaterThan(0L);
        await Assert.That(item.RecordedAt.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(item.RecordedAt).IsGreaterThanOrEqualTo(beforeWrite);
        await Assert.That(item.RecordedAt).IsLessThanOrEqualTo(TimeProvider.System.GetUtcNow());
        await Assert.That(stream.CutPosition).IsGreaterThanOrEqualTo(minimum);
        await Assert.That(stream.HasMore).IsFalse();
        await SqlRf3Protocol.EqualAsync(new EventSourceRef(context.Partition, FunctionalGateColdProtocol.Resource("topic", index), EventSourceKind.Topic), topic.Source);
        await SqlRf3Protocol.EqualAsync(new EventSourceHead(1, 1, 1), topic.Head);
        var publication = await Assert.That(topic.Events).HasSingleItem();
        await SqlRf3Protocol.EqualAsync(topic.Source, publication.Source);
        await SqlRf3Protocol.EqualAsync(FunctionalGateColdProtocol.Data(reference), publication.Data);
        await Assert.That(publication.Position).IsEqualTo(1L);
        await Assert.That(publication.EventSequence).IsGreaterThan(0L);
        await Assert.That(publication.RecordedAt.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(publication.RecordedAt).IsGreaterThanOrEqualTo(beforeWrite);
        await Assert.That(publication.RecordedAt).IsLessThanOrEqualTo(TimeProvider.System.GetUtcNow());
        await Assert.That(topic.CutPosition).IsGreaterThanOrEqualTo(minimum);
        await Assert.That(topic.HasMore).IsFalse();
        await Assert.That(message.Message).IsNotNull();
        var original = message.Message!;
        await Assert.That(original.Metadata.ReadySequence).IsGreaterThan(0L);
        await SqlRf3Protocol.EqualAsync(new MessageInspection(new(FunctionalGateColdProtocol.Id, MessageState.Ready,
            0, 1, original.Metadata.ReadySequence, null, null), FunctionalGateColdProtocol.Data(reference).PayloadJson, "{}"), original);
    }
}
