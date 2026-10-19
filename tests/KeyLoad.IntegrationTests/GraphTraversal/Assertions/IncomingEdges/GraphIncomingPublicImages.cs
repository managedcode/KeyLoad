using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphIncomingPublicImages
{
    internal static async Task<(DocumentResult[] Documents, OutboxStatus[] Outbox, GraphIncomingEdgesPage Incoming)>
        CaptureAsync(KeyLoadClient administrator, GraphIncomingClusterSeed seed, CancellationToken token)
    {
        var documents = new List<DocumentResult>();
        foreach (var reference in new[] { seed.Target, seed.LocalSource, seed.FirstSource, seed.SecondSource })
        {
            documents.Add((await PublicReadAssertions.DocumentAsync(await administrator.GetAsync(reference, token)))!);
        }
        var outbox = new List<OutboxStatus>();
        foreach (var partition in new[] { seed.TargetPartition, seed.FirstSourcePartition, seed.SecondSourcePartition })
        {
            outbox.Add(await McpCallerAssertions.SdkSuccessAsync(await administrator.OutboxStatusAsync(partition, token)));
        }
        var incoming = await McpCallerAssertions.SdkSuccessAsync(await administrator.IncomingEdgesAsync(
            GraphIncomingClusterScenario.Request(seed), token));
        return ([.. documents], [.. outbox], incoming);
    }

    internal static async Task SameAsync(
        (DocumentResult[] Documents, OutboxStatus[] Outbox, GraphIncomingEdgesPage Incoming) before,
        (DocumentResult[] Documents, OutboxStatus[] Outbox, GraphIncomingEdgesPage Incoming) after)
    {
        await SqlClusterProtocol.EqualAsync(before.Documents, after.Documents);
        await SqlClusterProtocol.EqualAsync(before.Outbox, after.Outbox);
        await SqlClusterProtocol.EqualAsync(before.Incoming.Version, after.Incoming.Version);
        await SqlClusterProtocol.EqualAsync(before.Incoming.Projection, after.Incoming.Projection);
        await SqlClusterProtocol.EqualAsync(before.Incoming.Rows, after.Incoming.Rows);
        await Assert.That(after.Incoming.CutPosition).IsGreaterThanOrEqualTo(before.Incoming.CutPosition);
    }

    internal static async Task LiteralDocumentsAsync(KeyLoadClient administrator, GraphIncomingClusterSeed seed,
        long localRevision, CancellationToken token)
    {
        foreach (var reference in new[] { seed.Target, seed.LocalSource, seed.FirstSource, seed.SecondSource })
        {
            var expected = new DocumentResult(reference, reference == seed.LocalSource ? localRevision
                : GraphIncomingPublicPolicy.InitialRevision, GraphIncomingClusterScenario.EmptyJson, false, []);
            await SqlClusterProtocol.EqualAsync(expected,
                await PublicReadAssertions.DocumentAsync(await administrator.GetAsync(reference, token)));
        }
    }
}
