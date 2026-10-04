using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadExecutionTests
{
    private const string Orders = "orders";
    private const string Links = "links";
    private const string EmbeddingPath = "/embedding";
    private const string TestName = "test";
    private const string TestVersion = "1";
    private const string RootIdentity = "root";
    private const string VertexA = "a";
    private const string FieldTextPath = "/text";
    private const int VectorDimensions = 2;
    private const int ZeroDepth = 0;
    private const int MaximumBatchBytes = 60;
    private const int QueryDeadlineSeconds = 5;
    private const int InitialReadBytes = 100;
    private const int AdditionalReadByte = 1;
    private const int DeadlineWaitSeconds = 6;
    private static VectorSpace Space { get; } = new(TestName, VectorDimensions, DistanceMetric.Cosine, TestName, TestVersion);

    [Test]
    public async Task SearchAndGraphResultBudgetsIncludeProtocolMetadata()
    {
        using var database = new TestDatabase();
        database.Configure(Orders, ResourceKind.Collection);
        database.Configure(Links, ResourceKind.Graph);
        database.Commit(new PutDocument(Orders, "a", "{\"text\":\"alpha\"}"));
        var bounded = new DatabaseEngine(database.Store, database.Database.Authorization, new() { MaxBatchBytes = MaximumBatchBytes });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => new SearchEngine(bounded)
            .Search(RootIdentity, new(database.Partition, Orders, FieldTextPath, "alpha"), TestContext.Current!.Execution.CancellationToken)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => bounded.Traverse(RootIdentity, database.Partition, Links,
            new(database.Partition, Orders, VertexA), maxDepth: ZeroDepth,
            cancellationToken: TestContext.Current!.Execution.CancellationToken)).Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task CancelledReadsStopBeforeTouchingCanonicalState()
    {
        using var database = new TestDatabase();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        await cancellation.CancelAsync();
        var position = database.Store.Position;
        await Assert.That(() => new SearchEngine(database.Database).Search(RootIdentity, VectorRequest(database), cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(() => database.Database.Traverse(RootIdentity, database.Partition, Links,
            new(database.Partition, Orders, VertexA), cancellationToken: cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task OneReadDeadlineCoversSubsequentPointAndScanWork()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var budget = new ReadExecutionBudget(new() { QueryDeadlineSeconds = QueryDeadlineSeconds }, TimeProvider.System, cancellationToken);
        budget.ChargeBytes(InitialReadBytes);
        await Task.Delay(TimeSpan.FromSeconds(DeadlineWaitSeconds), TimeProvider.System, cancellationToken);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => budget.ChargeBytes(AdditionalReadByte)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(budget.ChargeTextToken).Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static SearchRequest VectorRequest(TestDatabase database) => new(database.Partition, Orders,
        VectorField: EmbeddingPath, Vector: [1, 0], Space: Space);
}
