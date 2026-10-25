using KeyLoad.Client;
using KeyLoad.ClusterTests.QueryExecution;
using S = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateSearch;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateSearchText
{
    private const string FirstSuffix = "-a";
    private const string SecondSuffix = "-b";
    private const string Query = "needle";

    internal static async Task RunAsync(FunctionalGateContext ctx, string collection, string stem, CancellationToken token)
    {
        var first = stem + FirstSuffix;
        var second = stem + SecondSuffix;
        var created = await S.CommitAsync(ctx,
            [new PutDocument(collection, first, S.NeedleJson, S.Initial),
                new PutDocument(collection, second, S.OtherJson, S.Initial)],
            [new(S.Put, collection, first, S.Created), new(S.Put, collection, second, S.Created)], true, token);
        var request = new SearchRequest(ctx.Partition, collection, TextField: S.TextField, Text: Query,
            AllowedIds: [first, second]);
        await WaitAsync(ctx, collection, created.Token, token);
        await S.SearchAsync(ctx, request, [S.Hit(ctx, collection, first, S.Created, S.NeedleJson, S.FirstRank)], token);
        var updated = await S.CommitAsync(ctx,
            [new PutDocument(collection, first, S.OtherJson, S.Created),
                new PutDocument(collection, second, S.NeedleJson, S.Created)],
            [new(S.Put, collection, first, S.Updated), new(S.Put, collection, second, S.Updated)], false, token);
        await WaitAsync(ctx, collection, updated.Token, token);
        await S.SearchAsync(ctx, request, [S.Hit(ctx, collection, second, S.Updated, S.NeedleJson, S.FirstRank)], token);
        await S.LinkAndUnlinkAsync(ctx, collection, second, token);
        var deleted = await S.CommitAsync(ctx,
            [new DeleteDocument(collection, first, S.Updated), new DeleteDocument(collection, second, S.Updated)],
            [new(S.Delete, collection, first, S.Deleted), new(S.Delete, collection, second, S.Deleted)], true, token);
        await WaitAsync(ctx, collection, deleted.Token, token);
        await S.SearchAsync(ctx, request, [], token);
    }

    private static async Task WaitAsync(FunctionalGateContext ctx, string collection,
        CommitToken minimum, CancellationToken token)
    {
        var request = new WaitForIndexRequest(ctx.Partition, collection, S.TextField, minimum);
        await RequireWaitAsync(minimum, await ctx.SdkAsync(ctx.Reader.Sdk.WaitForIndexAsync(request, token)));
        await RequireWaitAsync(minimum, await ctx.McpAsync<WaitForIndexResult>(
            ctx.Reader, WaitForIndexProtocol.Tool, request, token));
    }

    private static async Task RequireWaitAsync(CommitToken minimum, WaitForIndexResult result)
    {
        await SqlClusterProtocol.EqualAsync(minimum with
        {
            Position = result.AppliedToken.Position
        }, result.AppliedToken);
        await Assert.That(result.AppliedToken.Position).IsGreaterThanOrEqualTo(minimum.Position);
        await Assert.That(result.SchemaVersion).IsEqualTo(S.Created);
        await Assert.That(result.PolicyEpoch).IsGreaterThan(S.Initial);
    }
}
