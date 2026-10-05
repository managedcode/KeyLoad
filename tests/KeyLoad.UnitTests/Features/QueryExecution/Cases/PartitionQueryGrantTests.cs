using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryGrantTests
{
    private const int SeedCount = 2;

    [Test]
    public async Task ActualNativeReadBytesAndExaminedRecordsAdmitExactlyAndRejectOneLess()
    {
        using var measured = PartitionQueryTestSupport.Create();
        var partitions = PartitionQueryTestSupport.EqualLengthPartitions(measured);
        var rows = SeedRows();
        foreach (var partition in partitions)
        {
            PartitionQueryTestSupport.AddRows(measured, partition, rows);
        }

        var measuredResult = Run(measured, partitions);
        var exactLimits = new DatabaseLimits
        {
            MaxScanRecords = measuredResult.ExaminedRecords,
            MaxQueryReadBytes = measuredResult.ReadBytes
        };
        using var exact = PartitionQueryTestSupport.Create(exactLimits);
        var exactPartitions = PartitionQueryTestSupport.EqualLengthPartitions(exact);
        foreach (var partition in exactPartitions)
        {
            PartitionQueryTestSupport.AddRows(exact, partition, rows);
        }

        var exactResult = Run(exact, exactPartitions);

        await Assert.That(exactResult.ExaminedRecords).IsEqualTo(measuredResult.ExaminedRecords);
        await Assert.That(exactResult.ReadBytes).IsEqualTo(measuredResult.ReadBytes);
        for (var index = 1; index < exactResult.Leaves.Length; index++)
        {
            await Assert.That(exactResult.Leaves[index].ExaminedRecords)
                .IsEqualTo(exactResult.Leaves[0].ExaminedRecords);
            await Assert.That(exactResult.Leaves[index].ReadBytes)
                .IsEqualTo(exactResult.Leaves[0].ReadBytes);
        }
        await AssertBudgetExceeded(measuredResult.ExaminedRecords - 1, measuredResult.ReadBytes, partitions.Length);
        await AssertBudgetExceeded(measuredResult.ExaminedRecords, measuredResult.ReadBytes - 1, partitions.Length);
    }

    [Test]
    public async Task MaxBatchAdmitsTheCompleteNativeMergeAtItsExactBoundAndNoRowsBelowIt()
    {
        using var measured = PartitionQueryTestSupport.Create();
        PartitionQueryTestSupport.AddRows(measured, measured.Partition, SeedRows());
        var measuredResult = Run(measured);
        var wireBytes = JsonDefaults.Serialize(measuredResult).Length;
        var retainedAdmission = PartitionQueryReferenceSizing.MinimumSingleLeafBatchBytes(measuredResult, SeedCount);
        var exactBatchBytes = checked((int)Math.Max(wireBytes, retainedAdmission));
        using var exact = PartitionQueryTestSupport.Create(new() { MaxBatchBytes = exactBatchBytes });
        PartitionQueryTestSupport.AddRows(exact, exact.Partition, SeedRows());
        var result = Run(exact);
        await Assert.That(result.Complete).IsTrue();

        using var below = PartitionQueryTestSupport.Create(new() { MaxBatchBytes = exactBatchBytes - 1 });
        PartitionQueryTestSupport.AddRows(below, below.Partition, SeedRows());
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Run(below));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task OneNativeGrantChargesIndexRangeAndDocumentPointReadsTogether()
    {
        using var measured = PartitionQueryTestSupport.CreateIndexed();
        PartitionQueryTestSupport.AddIndexedRows(measured, measured.Partition);
        var measuredResult = RunIndexed(measured);
        await Assert.That(measuredResult.Leaves[0].AccessPath).IsEqualTo("index:by-status");
        await Assert.That(measuredResult.ExaminedRecords).IsGreaterThan(3);

        using var exact = PartitionQueryTestSupport.CreateIndexed(new()
        {
            MaxScanRecords = measuredResult.ExaminedRecords,
            MaxQueryReadBytes = measuredResult.ReadBytes
        });
        PartitionQueryTestSupport.AddIndexedRows(exact, exact.Partition);
        var exactResult = RunIndexed(exact);
        await Assert.That(exactResult.ExaminedRecords).IsEqualTo(measuredResult.ExaminedRecords);
        await Assert.That(exactResult.ReadBytes).IsEqualTo(measuredResult.ReadBytes);

        using var below = PartitionQueryTestSupport.CreateIndexed(new()
        {
            MaxScanRecords = measuredResult.ExaminedRecords - 1,
            MaxQueryReadBytes = measuredResult.ReadBytes
        });
        PartitionQueryTestSupport.AddIndexedRows(below, below.Partition);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => RunIndexed(below));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static PartitionQuerySeed[] SeedRows()
        => [new("budget-a", 2, "alpha"), new("budget-b", 1, "beta")];

    private static PartitionQueryResultV1 Run(TestDatabase database)
        => new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).ExecutePartitionQuery(PartitionQueryTestSupport.Principal,
            PartitionQueryTestSupport.Request(database, SeedCount), [database.Partition]);

    private static PartitionQueryResultV1 Run(TestDatabase database, PartitionRef[] partitions)
        => new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).ExecutePartitionQuery(PartitionQueryTestSupport.Principal,
            PartitionQueryTestSupport.Request(database, SeedCount), [.. partitions]);

    private static PartitionQueryResultV1 RunIndexed(TestDatabase database)
        => new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).ExecutePartitionQuery(PartitionQueryTestSupport.Principal,
            PartitionQueryTestSupport.IndexedRequest(database, 2), [database.Partition]);

    private static async Task AssertBudgetExceeded(int maxScanRecords, long maxReadBytes, int partitionCount)
    {
        using var database = PartitionQueryTestSupport.Create(new()
        {
            MaxScanRecords = maxScanRecords,
            MaxQueryReadBytes = maxReadBytes
        });
        var partitions = PartitionQueryTestSupport.EqualLengthPartitions(database)[..partitionCount];
        foreach (var partition in partitions)
        {
            PartitionQueryTestSupport.AddRows(database, partition, SeedRows());
        }

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Run(database, partitions));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }
}
