using System.Diagnostics;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryCancellationTests
{
    private const int SeedCount = 5_000;
    private static readonly TimeSpan ObservationBound = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ObservedNativeReadProgressCancelsTheOriginalBudgetAndLeavesNoPartialResult()
    {
        using var database = PartitionQueryTestSupport.Create();
        SeedRows(database);
        using var cancellation = new CancellationTokenSource();
        var budget = new ReadExecutionBudget(database.Database.Limits, cancellationToken: cancellation.Token);
        var engine = new QueryEngine(database.Database);
        var operation = Task.Run(() => engine.ExecutePartitionQuery(PartitionQueryTestSupport.Principal,
            PartitionQueryTestSupport.Request(database, 1), [database.Partition], budget));
        var observedBytes = await WaitForReadProgressAsync(budget, operation);
        await cancellation.CancelAsync();
        OperationCanceledException? failure = null;
        try
        {
            _ = await operation;
        }
        catch (OperationCanceledException error)
        {
            failure = error;
        }

        await Assert.That(observedBytes).IsGreaterThan(0L);
        await Assert.That(failure?.CancellationToken).IsEqualTo(cancellation.Token);
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

    private static async Task<long> WaitForReadProgressAsync(ReadExecutionBudget budget,
        Task<PartitionQueryResultV1> operation)
    {
        var started = Stopwatch.GetTimestamp();
        while (budget.ReadBytes == 0 && !operation.IsCompleted
               && Stopwatch.GetElapsedTime(started) < ObservationBound)
        {
            await Task.Yield();
        }
        return budget.ReadBytes;
    }
}
