using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphIncomingPublicImages
{
    internal static async Task<(DocumentResult[] Documents, OutboxStatus[] Outbox, GraphIncomingEdgesPageV1 Incoming)>
        CaptureAsync(KeyLoadClient administrator, GraphIncomingRf3Seed seed, CancellationToken token)
    {
        var documents = new List<DocumentResult>();
        foreach (var reference in new[] { seed.Target, seed.LocalSource, seed.FirstSource, seed.SecondSource })
        { documents.Add((await McpCallerAssertions.SdkSuccessAsync(await administrator.GetAsync(reference, token)))!); }
        var outbox = new List<OutboxStatus>();
        foreach (var partition in new[] { seed.TargetPartition, seed.FirstSourcePartition, seed.SecondSourcePartition })
        { outbox.Add(await McpCallerAssertions.SdkSuccessAsync(await administrator.OutboxStatusAsync(partition, token))); }
        var incoming = await McpCallerAssertions.SdkSuccessAsync(await administrator.IncomingEdgesAsync(
            GraphIncomingRf3Scenario.Request(seed), token));
        return ([.. documents], [.. outbox], incoming);
    }

    internal static async Task SameAsync(
        (DocumentResult[] Documents, OutboxStatus[] Outbox, GraphIncomingEdgesPageV1 Incoming) before,
        (DocumentResult[] Documents, OutboxStatus[] Outbox, GraphIncomingEdgesPageV1 Incoming) after)
    {
        await SqlRf3Protocol.EqualAsync(before.Documents, after.Documents);
        await SqlRf3Protocol.EqualAsync(before.Outbox, after.Outbox);
        await SqlRf3Protocol.EqualAsync(before.Incoming.Version, after.Incoming.Version);
        await SqlRf3Protocol.EqualAsync(before.Incoming.Projection, after.Incoming.Projection);
        await SqlRf3Protocol.EqualAsync(before.Incoming.Rows, after.Incoming.Rows);
        await Assert.That(after.Incoming.CutPosition).IsGreaterThanOrEqualTo(before.Incoming.CutPosition);
    }

    internal static async Task LiteralDocumentsAsync(KeyLoadClient administrator, GraphIncomingRf3Seed seed,
        long localRevision, CancellationToken token)
    {
        foreach (var reference in new[] { seed.Target, seed.LocalSource, seed.FirstSource, seed.SecondSource })
        {
            var expected = new DocumentResult(reference, reference == seed.LocalSource ? localRevision
                : GraphIncomingPublicPolicy.InitialRevision, GraphIncomingRf3Scenario.EmptyJson, false, []);
            await SqlRf3Protocol.EqualAsync(expected,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.GetAsync(reference, token)));
        }
    }
}
