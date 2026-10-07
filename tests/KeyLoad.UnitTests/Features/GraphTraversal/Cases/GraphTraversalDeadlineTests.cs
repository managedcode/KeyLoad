using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

/// <summary>KL-023 / AC-GRAPH-003/004: elapsed time stops an admitted real traversal without partial success or writes.</summary>
internal sealed class GraphTraversalDeadlineTests
{
    private const string Orders = "deadline-orders";
    private const string Links = "deadline-links";
    private const string Root = "root";
    private const string A = "a";
    private const string B = "b";
    private const string C = "c";
    private const string AB = "ab";
    private const string BC = "bc";
    private const string CA = "ca";
    private const string Label = "next";
    private const string DocumentJson = "{\"deadline\":\"persisted-canary\"}";
    private const string DeadlineDiagnostic = "The read execution deadline is exceeded.";
    private const int DeadlineSeconds = 1;
    private const int BeyondDeadlineSeconds = 2;
    private const int SnapshotRecordCapacity = 4_096;
    private const int CycleDepth = 3;
    private const long InitialRevision = 1;

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ElapsedCapDuringNativeAdjacencyWorkRejectsWithoutPartialPageAndHealthyTraversalFollows(int examinedEdges)
    {
        var clock = new GraphTraversalDeadlineClock();
        using var database = new TestDatabase(new() { QueryDeadlineSeconds = DeadlineSeconds }, timeProvider: clock);
        database.Configure(Orders, ResourceKind.Collection);
        database.Configure(Links, ResourceKind.Graph);
        EntityRef Vertex(string id) => new(database.Partition, Orders, id);
        var commit = database.Commit(new PutDocument(Orders, A, DocumentJson),
            new PutDocument(Orders, B, DocumentJson), new PutDocument(Orders, C, DocumentJson),
            new UpsertEdge(Links, AB, Vertex(A), Vertex(B), Label),
            new UpsertEdge(Links, BC, Vertex(B), Vertex(C), Label),
            new UpsertEdge(Links, CA, Vertex(C), Vertex(A), Label));
        var token = TestContext.Current!.Execution.CancellationToken;
        var healthy = database.Database.Traverse(Root, database.Partition, Links, Vertex(A), maxDepth: CycleDepth,
            cancellationToken: token);
        await AssertHealthyAsync(healthy, database.Partition);
        var before = CaptureState(database);
        await Assert.That(before.HasMore).IsFalse();
        await Assert.That(before.Records.Length).IsGreaterThan(0);
        var position = database.Store.Position;
        await Assert.That(position).IsEqualTo(commit.Token.Position);
        var reads = database.Store.GetReadDiagnostics();
        var rangeThreshold = reads.RangeBaselineEntries + reads.RangeStagedEntries + examinedEdges;
        clock.ArmAfterNativeWork(() =>
        {
            var current = database.Store.GetReadDiagnostics();
            return current.RangeBaselineEntries + current.RangeStagedEntries >= rangeThreshold;
        }, TimeSpan.FromSeconds(BeyondDeadlineSeconds));
        global::KeyLoad.GraphTraversal? partial = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = database.Database.Traverse(Root,
            database.Partition, Links, Vertex(A), maxDepth: CycleDepth, cancellationToken: token));
        clock.StopAdvancing();
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo(DeadlineDiagnostic);
        await Assert.That(clock.ElapsedCapTriggered).IsTrue();
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(reads.RangeExaminedBytes);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await AssertBytesEqualAsync(before, CaptureState(database));
        var after = database.Database.Traverse(Root, database.Partition, Links, Vertex(A), maxDepth: CycleDepth,
            cancellationToken: token);
        await AssertHealthyAsync(after, database.Partition);
        await AssertBytesEqualAsync(healthy, after);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await AssertBytesEqualAsync(before, CaptureState(database));
    }

    private static ScanPage CaptureState(TestDatabase database)
        => database.Store.Read(view => view.Scan([], SnapshotRecordCapacity));

    private static async Task AssertHealthyAsync(global::KeyLoad.GraphTraversal page, PartitionRef partition)
    {
        await Assert.That(page.Vertices).IsEquivalentTo(new[]
        { new EntityRef(partition, Orders, A), new(partition, Orders, B), new(partition, Orders, C) }, CollectionOrdering.Matching);
        await Assert.That(page.Edges.Select(edge => edge.Id)).IsEquivalentTo(new[] { AB, BC, CA }, CollectionOrdering.Matching);
        await Assert.That(page.Edges.Select(edge => edge.From)).IsEquivalentTo(new[]
        { new EntityRef(partition, Orders, A), new(partition, Orders, B), new(partition, Orders, C) }, CollectionOrdering.Matching);
        await Assert.That(page.Edges.Select(edge => edge.To)).IsEquivalentTo(new[]
        { new EntityRef(partition, Orders, B), new(partition, Orders, C), new(partition, Orders, A) }, CollectionOrdering.Matching);
        await Assert.That(page.Edges.All(edge => edge.Label == Label)).IsTrue();
        await Assert.That(page.Edges.All(edge => edge.Revision == InitialRevision)).IsTrue();
    }

    private static async Task AssertBytesEqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
