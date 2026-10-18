using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateCompositionProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateCompositionAssertions
{
    internal static async Task AnchorAsync(FunctionalGateContext context, CancellationToken token)
    {
        var actual = await context.SdkAsync(context.Reader.Sdk.GetAsync(context.Bind(context.Anchor), token));
        await SqlClusterProtocol.EqualAsync(new DocumentReadResult(new(context.Bind(context.Anchor), P.Initial,
            P.AnchorJson, false, [])), actual);
    }

    internal static async Task PairAsync(FunctionalGateContext context, FunctionalGateCompositionState state,
        CommitReceipt minimum, bool linked, CancellationToken token)
    {
        foreach (var official in new[] { false, true })
        {
            await RowAsync(context, state.Collection, state.Id, state.Document(context, linked),
                linked ? P.Initial : P.UnlinkedDocument, minimum, official, token);
            await RowAsync(context, P.Table, state.Id, state.Row(context, linked),
                linked ? P.LinkedTable : P.UnlinkedTable, minimum, official, token);
        }
    }

    private static async Task RowAsync(FunctionalGateContext context, string collection, string id, string json,
        long revision, CommitReceipt minimum, bool official, CancellationToken token)
    {
        var page = await FunctionalGateCompositionCalls.SqlAsync<QueryPage>(context,
            FunctionalGateCompositionCalls.Read(context, collection, id), official, false, token);
        await SqlClusterProtocol.EqualAsync(new QueryRow[] { new(id, revision, json, false, []) }, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(P.Point);
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(minimum.Token.Position);
    }

    internal static async Task MissingAsync(FunctionalGateContext context, FunctionalGateCompositionState state,
        CommitReceipt minimum, CancellationToken token)
    {
        foreach (var collection in new[] { state.Collection, P.Table })
        {
            var page = await FunctionalGateCompositionCalls.SqlAsync<QueryPage>(context,
                FunctionalGateCompositionCalls.Read(context, collection, state.Id), true, false, token);
            await Assert.That(page.Rows).IsEmpty();
            await Assert.That(page.Cursor).IsNull();
            await Assert.That(page.AccessPath).IsEqualTo(P.Point);
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(minimum.Token.Position);
        }
        await AnchorAsync(context, token);
    }
}
