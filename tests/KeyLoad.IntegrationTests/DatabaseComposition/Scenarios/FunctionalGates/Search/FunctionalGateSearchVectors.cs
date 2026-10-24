using S = KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateSearch;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateSearchVectors
{
    private const string FirstSuffix = "-a";
    private const string SecondSuffix = "-b";

    internal static async Task RunAsync(FunctionalGateContext ctx, string collection, string stem, CancellationToken token)
    {
        var first = stem + FirstSuffix;
        var second = stem + SecondSuffix;
        await S.CommitAsync(ctx,
            [new PutDocument(collection, first, S.OriginalJson, S.Initial),
                new PutDocument(collection, second, S.OriginalJson, S.Initial),
                new PutVector(collection, first, S.VectorField, [1, 0], S.Space, S.Created),
                new PutVector(collection, second, S.VectorField, [0.5f, 0.5f], S.Space, S.Created)],
            [new(S.Put, collection, first, S.Created), new(S.Put, collection, second, S.Created),
                new(S.VectorPut, collection, first, S.Created), new(S.VectorPut, collection, second, S.Created)], false, token);
        var request = new SearchRequest(ctx.Partition, collection, VectorField: S.VectorField,
            Vector: [1, 0], Space: S.Space, AllowedIds: [first, second]);
        await S.SearchAsync(ctx, request,
            [S.Hit(ctx, collection, first, S.Created, S.OriginalJson, S.FirstRank),
                S.Hit(ctx, collection, second, S.Created, S.OriginalJson, S.SecondRank)], token);
        await S.CommitAsync(ctx, [new PutDocument(collection, first, S.UpdatedJson, S.Created),
                new PutVector(collection, first, S.VectorField, [0, 1], S.Space, S.Updated)],
            [new(S.Put, collection, first, S.Updated), new(S.VectorPut, collection, first, S.Updated)], true, token);
        await S.SearchAsync(ctx, request,
            [S.Hit(ctx, collection, second, S.Created, S.OriginalJson, S.FirstRank),
                S.Hit(ctx, collection, first, S.Updated, S.UpdatedJson, S.SecondRank)], token);
        await S.LinkAndUnlinkAsync(ctx, collection, second, token);
        await S.CommitAsync(ctx, [new DeleteDocument(collection, first, S.Updated),
                new DeleteDocument(collection, second, S.Created)],
            [new(S.Delete, collection, first, S.Deleted), new(S.Delete, collection, second, S.Updated)], false, token);
        await S.SearchAsync(ctx, request, [], token);
    }
}
