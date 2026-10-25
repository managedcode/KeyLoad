using KeyLoad.Query;

namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class ReadExecutionTests
{
    private const string Orders = "orders";
    private const string Links = "links";
    private const string EmbeddingPath = "/embedding";
    private const string TestName = "test";
    private const string TestVersion = "1";
    private const string RootIdentity = "root";
    private const string VertexA = "a";
    private const int VectorDimensions = 2;
    private static VectorSpace Space { get; } = new(TestName, VectorDimensions, DistanceMetric.Cosine, TestName, TestVersion);

    [Test]
    public async Task CancelledReadsStopBeforeTouchingCanonicalState()
    {
        using var database = new TestDatabase();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        await cancellation.CancelAsync();
        var position = database.Store.Position;
        await Assert.That(() => new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).Search(RootIdentity, VectorRequest(database), cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(() => database.Database.Traverse(RootIdentity, database.Partition, Links,
            new(database.Partition, Orders, VertexA), cancellationToken: cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    private static SearchRequest VectorRequest(TestDatabase database) => new(database.Partition, Orders,
        VectorField: EmbeddingPath, Vector: [1, 0], Space: Space);
}
