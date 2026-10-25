using KeyLoad.Client;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateColdSearch
{
    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateColdModel model, CancellationToken token)
    {
        var text = FunctionalGateColdProtocol.Resource("text", model.Index);
        var wait = new WaitForIndexRequest(context.Partition, text, "/text", model.Receipt.Token);
        var direct = await context.SdkAsync(context.Reader.Sdk.WaitForIndexAsync(wait, token));
        var official = await context.McpAsync<WaitForIndexResult>(context.Reader, WaitForIndexProtocol.Tool, wait, token);
        foreach (var result in new[] { direct, official })
        {
            await SqlClusterProtocol.EqualAsync(model.Receipt.Token with
            {
                Position = result.AppliedToken.Position
            }, result.AppliedToken);
            await Assert.That(result.AppliedToken.Position).IsGreaterThanOrEqualTo(model.Receipt.Token.Position);
            await Assert.That(result.SchemaVersion).IsEqualTo(1L);
            await Assert.That(result.PolicyEpoch).IsGreaterThan(0L);
        }
        var textRequest = new SearchRequest(context.Partition, text, TextField: "/text", Text: "needle", AllowedIds: [FunctionalGateColdProtocol.Id]);
        await FunctionalGateSearch.SearchAsync(context, textRequest,
            [FunctionalGateSearch.Hit(context, text, FunctionalGateColdProtocol.Id, 1, FunctionalGateColdProtocol.Text, 1d / 61)], token);
        var vector = FunctionalGateColdProtocol.Resource("vectors", model.Index);
        var vectorRequest = new SearchRequest(context.Partition, vector, VectorField: "/embedding", Vector: [1, 0],
            Space: FunctionalGateSearch.Space, AllowedIds: [FunctionalGateColdProtocol.Id]);
        await FunctionalGateSearch.SearchAsync(context, vectorRequest,
            [FunctionalGateSearch.Hit(context, vector, FunctionalGateColdProtocol.Id, 1, FunctionalGateColdProtocol.Vector, 1d / 61)], token);
    }
}
