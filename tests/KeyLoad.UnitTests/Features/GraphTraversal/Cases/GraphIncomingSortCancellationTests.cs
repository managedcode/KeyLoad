using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphIncomingSortCancellationTests
{
    private const string TargetPartitionKey = "sort-target-partition";
    private const string TargetId = "sort-target-vertex";
    private const string SourcePrefix = "sort-source-";
    private const string EdgePrefix = "sort-edge-";
    private const string DeadlineDiagnostic = "The read execution deadline is exceeded.";
    private const string Label = "sort-links";
    private const int IncomingCount = 8;

    [Test]
    public async Task FullNativePageSortPreservesCancellationAndDeadlineAndHealthyReadFollows()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase(
            new DatabaseLimits { MaxResults = IncomingCount });
        var partition = GraphCrossPartitionTestSupport.Partition(TargetPartitionKey);
        var target = GraphCrossPartitionTestSupport.Vertex(partition, TargetId);
        var sources = CreateSources(partition);
        SeedEdges(database, partition, sources, target);
        var fullPage = ReadPage(database, target);
        await AssertOrderedSourcesAsync(fullPage, sources);

        await AssertCancellationPreservedAsync(database, fullPage, target);
        await AssertDeadlinePreservedAsync(database, fullPage, target);

        var healthy = ReadPage(database, target);
        await AssertOrderedSourcesAsync(healthy, sources);
    }

    private static async Task AssertCancellationPreservedAsync(TestDatabase database,
        GraphIncomingEdgesPageV1 page, EntityRef target)
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new GraphIncomingSortCancellationTimeProvider(cancellation);
        var budget = CreateBudget(database, clock, cancellation.Token);
        var builder = CreateBuilder(database, page, target, budget);
        clock.CancelAfterTimestampCalls(2);

        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => builder.Build());
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(clock.CancellationTriggered).IsTrue();
    }

    private static async Task AssertDeadlinePreservedAsync(TestDatabase database,
        GraphIncomingEdgesPageV1 page, EntityRef target)
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new GraphIncomingSortCancellationTimeProvider(cancellation);
        var budget = CreateBudget(database, clock, cancellation.Token);
        var builder = CreateBuilder(database, page, target, budget);
        clock.ExceedDeadlineAfterTimestampCalls(2);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => builder.Build());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo(DeadlineDiagnostic);
        await Assert.That(clock.DeadlineTriggered).IsTrue();
    }

    private static ReadExecutionBudget CreateBudget(TestDatabase database, TimeProvider clock,
        CancellationToken cancellationToken)
        => new(UnitExecutionOptions.DatabaseLimits(database.Database.Limits with { QueryDeadlineSeconds = 1 }), clock, cancellationToken);

    private static GraphIncomingEdgesPageBuilder CreateBuilder(TestDatabase database,
        GraphIncomingEdgesPageV1 page, EntityRef target, ReadExecutionBudget budget)
    {
        var (principal, graph) = ReadProjectionScope(database, budget, target.Partition);
        var builder = new GraphIncomingEdgesPageBuilder(database.Database, principal,
            IncomingCount, budget, page.CutPosition);
        foreach (var row in page.Rows)
        {
            builder.Add(row.Edge, row.DeliveredRevision, graph);
        }
        return builder;
    }

    private static GraphIncomingEdgesPageV1 ReadPage(TestDatabase database, EntityRef target)
        => database.Database.ReadIncomingGraphEdges(GraphCrossPartitionTestSupport.Root,
            new ReadIncomingGraphEdgesRequestV1(1, target, GraphCrossPartitionTestSupport.Graph, IncomingCount));

    private static EntityRef[] CreateSources(PartitionRef partition)
    {
        var sources = new EntityRef[IncomingCount];
        for (var index = 0; index < IncomingCount; index++)
        {
            sources[index] = GraphCrossPartitionTestSupport.Vertex(partition,
                SourcePrefix + (IncomingCount - index).ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sources;
    }

    private static void SeedEdges(TestDatabase database, PartitionRef partition,
        EntityRef[] sources, EntityRef target)
    {
        var vertices = new EntityRef[IncomingCount + 1];
        Array.Copy(sources, vertices, IncomingCount);
        vertices[IncomingCount] = target;
        GraphCrossPartitionTestSupport.SeedVertices(database, vertices);
        var mutations = new Mutation[IncomingCount];
        for (var index = 0; index < IncomingCount; index++)
        {
            mutations[index] = new UpsertEdge(GraphCrossPartitionTestSupport.Graph,
                EdgePrefix + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                sources[index], target, Label);
        }
        GraphCrossPartitionTestSupport.Commit(database, partition, mutations);
    }

    private static (PrincipalRecord Principal, ResourceDefinition Graph) ReadProjectionScope(
        TestDatabase database, ReadExecutionBudget budget, PartitionRef partition)
        => database.Store.Read(view =>
        {
            var grant = budget.CreateReadGrant(database.Database.Limits.MaxQueryReadBytes,
                database.Database.Limits.MaxScanRecords);
            var principal = GraphIncomingReadScope.ReadPrincipal(view, grant,
                GraphCrossPartitionTestSupport.Root, TimeProvider.System.GetUtcNow());
            var graph = GraphIncomingReadScope.ReadGraphResource(view, grant, partition,
                GraphCrossPartitionTestSupport.Graph);
            return (principal, graph);
        });

    private static async Task AssertOrderedSourcesAsync(GraphIncomingEdgesPageV1 page,
        EntityRef[] inputSources)
    {
        var expected = inputSources.OrderBy(source => source.Id, StringComparer.Ordinal).ToArray();
        await Assert.That(page.Rows.Length).IsEqualTo(IncomingCount);
        await Assert.That(page.Rows.Select(row => row.Edge.From).SequenceEqual(expected)).IsTrue();
    }
}
