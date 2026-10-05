using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryCancellationTests
{
    private const int SeedCount = 5_000;

    [Test]
    public async Task ObservedNativeReadProgressCancelsTheOriginalBudgetAndLeavesNoPartialResult()
    {
        using var database = PartitionQueryTestSupport.Create();
        SeedRows(database);
        using var cancellation = new CancellationTokenSource();
        var budget = new ReadExecutionBudget(database.Database.Limits, cancellationToken: cancellation.Token);
        var engine = new QueryEngine(database.Database);
        var outcome = PartitionQueryCancellationRun.Execute(database, engine, cancellation, budget);

        await Assert.That(outcome.QueryReturned).IsFalse();
        await Assert.That(outcome.Cancellation?.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(outcome.CancellationRequested).IsTrue();
        await Assert.That(outcome.ObservedReadBytes).IsGreaterThan(0L);
        var healthy = engine.ExecutePartitionQuery(PartitionQueryTestSupport.Principal,
            PartitionQueryTestSupport.Request(database, 1), [database.Partition]);
        await Assert.That(healthy.Complete).IsTrue();
        await Assert.That(healthy.Rows).HasSingleItem();
    }

    private static void SeedRows(TestDatabase database)
    {
        const int batchSize = 100;
        for (var start = 0; start < SeedCount; start += batchSize)
        {
            var count = Math.Min(batchSize, SeedCount - start);
            var rows = Enumerable.Range(start, count).Select(index => new PartitionQuerySeed(
                "cancel-" + index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture), index, "value"))
                .ToArray();
            PartitionQueryTestSupport.AddRows(database, database.Partition, rows);
        }
    }
}
