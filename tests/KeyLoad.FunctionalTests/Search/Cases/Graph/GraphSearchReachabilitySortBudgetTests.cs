using KeyLoad.Core.GraphTraversal;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class GraphSearchReachabilitySortBudgetTests
{
    private const string PrincipalId = "root";
    private const string FirstId = "sort-alpha";
    private const string SecondId = "sort-beta";
    private const string ThirdId = "sort-gamma";
    private const int SeedCount = 3;

    [Test]
    public async Task ReachabilitySortPreservesCancellationAndHealthyFollowup()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        SeedVertices(database);
        var seeds = CreateSeeds(database);
        var walk = new GraphWalkSpec(GraphSearchTestSupport.Graph, [.. seeds], 0, SeedCount, 1);

        var baselineClock = new GraphSearchReachabilitySortClock();
        var baseline = ReadReachability(database, walk, baselineClock, CancellationToken.None);
        await AssertOrdered(database, baseline, seeds);

        await AssertCancellation(database, walk, baselineClock.TimestampCalls - 2);
        await AssertOrdered(database, ReadReachability(database, walk, TimeProvider.System, CancellationToken.None), seeds);
    }

    private static async Task AssertCancellation(TestDatabase database, GraphWalkSpec walk, long cancelAt)
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new GraphSearchReachabilitySortClock(cancellation, cancelAt: cancelAt);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            ReadReachability(database, walk, clock, cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(clock.CancellationTriggered).IsTrue();
    }

    private static GraphSearchReachability[] ReadReachability(TestDatabase database, GraphWalkSpec walk,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var budget = new ReadExecutionBudget(clock, cancellationToken);
        return database.Database.WithQueryView(PrincipalId, database.Partition,
            GraphSearchTestSupport.Documents, (view, principal, _) =>
                database.Database.ReadGraphSearchReachability(view, principal, database.Partition, walk, budget));
    }

    private static EntityRef[] CreateSeeds(TestDatabase database)
        => [GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, ThirdId),
            GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, FirstId),
            GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, SecondId)];

    private static void SeedVertices(TestDatabase database)
        => database.Commit(new PutDocument(GraphSearchTestSupport.Projects, FirstId, "{}"),
            new PutDocument(GraphSearchTestSupport.Projects, SecondId, "{}"),
            new PutDocument(GraphSearchTestSupport.Projects, ThirdId, "{}"));

    private static async Task AssertOrdered(TestDatabase database, GraphSearchReachability[] actual, EntityRef[] seeds)
    {
        var expected = seeds.OrderBy(seed => seed.Id, StringComparer.Ordinal).Select(database.ExpectedReference).ToArray();
        await Assert.That(actual.Select(entry => entry.Reference).SequenceEqual(expected)).IsTrue();
        await Assert.That(actual.Select(entry => entry.ShortestHops).SequenceEqual([0, 0, 0])).IsTrue();
    }
}
