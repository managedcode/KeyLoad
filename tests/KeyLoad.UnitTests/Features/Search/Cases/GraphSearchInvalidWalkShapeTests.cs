using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GraphSearchInvalidWalkShapeTests
{
    private const string InvalidLabel = "bad\u0000label";
    private const string PrincipalId = "root";
    private const int VersionOne = 1;

    [Test]
    public async Task DefaultAndMalformedWalkShapesFailValidationWithoutChangingPosition()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var engine = new SearchEngine(database.Database);
        var seed = GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects,
            GraphSearchTestSupport.Root);
        var position = database.Store.Position;

        foreach (var request in InvalidRequests(database, seed))
        {
            var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.GraphSearchAsync(
                PrincipalId, request, TestContext.Current!.Execution.CancellationToken)))!;
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(database.Store.Position).IsEqualTo(position);
        }

        var healthy = await engine.GraphSearchAsync(PrincipalId, ValidRequest(database, seed),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(healthy.Hits.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEquivalentTo([GraphSearchTestSupport.FirstHit, GraphSearchTestSupport.SecondHit],
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static GraphSearchRequest[] InvalidRequests(TestDatabase database, EntityRef seed)
    {
        var search = new SearchRequest(database.Partition, GraphSearchTestSupport.Documents);
        var walk = GraphSearchTestSupport.Walk(seed);
        return
        [
            new(VersionOne, search, Retriever: new(walk with { Seeds = default })),
            new(VersionOne, search, Retriever: new(walk with { Labels = default(ImmutableArray<string>) })),
            new(VersionOne, search, Retriever: new(walk with { Labels = [InvalidLabel] }))
        ];
    }

    private static GraphSearchRequest ValidRequest(TestDatabase database, EntityRef seed)
        => new(VersionOne, new(database.Partition, GraphSearchTestSupport.Documents),
            Retriever: new(GraphSearchTestSupport.Walk(seed)));
}
