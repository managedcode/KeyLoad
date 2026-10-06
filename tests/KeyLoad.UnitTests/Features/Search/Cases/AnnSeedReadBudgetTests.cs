using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedReadBudgetTests
{
    private const string LineageKeySpace = "vector-projection-lineage";
    [Test]
    public async Task ActualReadBytesPassAtObservedLimitAndFailOneByteBelowWithoutMutation()
    {
        using var database = AnnSeedTestSupport.Create(8);
        var measured = AnnSeedTestSupport.Capture(database);
        var position = database.Store.Position;
        var exactBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits with
        { MaxQueryReadBytes = measured.ReadBytes }));
        var exact = AnnSeedTestSupport.Capture(database, budget: exactBudget);
        var shortBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits with
        { MaxQueryReadBytes = measured.ReadBytes - 1 }));
        var failure = AnnSeedTestSupport.CaptureFailure(database, AnnSeedTestSupport.Principal, budget: shortBudget);

        await Assert.That(measured.ReadBytes).IsGreaterThan(0L);
        await Assert.That(exact.ReadBytes).IsEqualTo(measured.ReadBytes);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task MetadataVectorAndDocumentReadsAreChargedExactlyOnce()
    {
        using var database = CreateVisibilitySource();
        var expectedBytes = MeasureSourceBytes(database);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        var seed = AnnSeedTestSupport.Capture(database, budget: budget);

        await Assert.That(seed.Records.Select(record => record.DocumentId).ToArray())
            .IsEquivalentTo(["visible"], CollectionOrdering.Matching);
        await Assert.That(seed.ReadBytes).IsEqualTo(expectedBytes);
        await Assert.That(budget.ReadBytes).IsEqualTo(expectedBytes);
    }

    [Test]
    public async Task ScanLookaheadFailureNeverReturnsPartialSeed()
    {
        using var database = AnnSeedTestSupport.Create(3, new() { MaxScanRecords = 2 });
        var position = database.Store.Position;
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        var expectedBytes = MeasureSourceBytes(database, maxScanRecords: 2);
        var failure = AnnSeedTestSupport.CaptureFailure(database, AnnSeedTestSupport.Principal, budget: budget);

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(budget.ReadBytes).IsEqualTo(expectedBytes);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    private static TestDatabase CreateVisibilitySource()
    {
        var database = AnnSeedTestSupport.Create(0);
        database.Commit(
            new PutDocument(AnnSeedTestSupport.Collection, "visible", "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, "visible", AnnSeedTestSupport.Field,
                [1, 0, 0], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, "hidden", "{}", Access: new("other-owner")),
            new PutVector(AnnSeedTestSupport.Collection, "hidden", AnnSeedTestSupport.Field,
                [0, 1, 0], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, "stale", "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, "stale", AnnSeedTestSupport.Field,
                [0, 0, 1], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, "deleted", "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, "deleted", AnnSeedTestSupport.Field,
                [1, 1, 0], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, "other-space", "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, "other-space", AnnSeedTestSupport.Field,
                [0.5f, 0.5f, 0.5f], AnnSeedTestSupport.Space(model: "different-model"), 1),
            new PutDocument(AnnSeedTestSupport.Collection, "missing-document", "{}",
                Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, "missing-document", AnnSeedTestSupport.Field,
                [0.25f, 0.75f, 0.5f], AnnSeedTestSupport.Space(), 1));
        database.Store.Commit((transaction, _) =>
        {
            transaction.Delete(DocumentStorageKeys.RecordKey(database.Partition, AnnSeedTestSupport.Collection,
                "missing-document"));
            return true;
        });
        database.Commit(new PatchDocument(AnnSeedTestSupport.Collection, "stale",
            [new("/state", PatchKind.Set, "1")], 1));
        database.Commit(new DeleteDocument(AnnSeedTestSupport.Collection, "deleted", 1));
        return database;
    }

    private static long MeasureSourceBytes(TestDatabase database, int? maxScanRecords = null)
        => database.Store.Read(view =>
        {
            var counter = new AnnSeedReadByteCounter();
            CountRead(view, KeySpace.Principal(AnnSeedTestSupport.Principal), counter);
            CountRead(view, KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId,
                AnnSeedTestSupport.Collection), counter);
            CountRead(view, KeySpace.Partition("outbox-head", database.Partition), counter);
            CountRead(view, KeySpace.Applied.ToArray(), counter);
            view.VisitRange(KeySpace.Partition("vector", database.Partition, AnnSeedTestSupport.Collection,
                AnnSeedTestSupport.Field), maxScanRecords ?? database.Database.Limits.MaxScanRecords, (_, value) =>
                {
                    var vector = NativeSerialization.Deserialize<VectorRecord>(value);
                    CountRead(view, DocumentStorageKeys.RecordKey(database.Partition, AnnSeedTestSupport.Collection,
                        vector.DocumentId), counter);
                    var document = view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(database.Partition,
                        AnnSeedTestSupport.Collection, vector.DocumentId));
                    var principal = view.GetRecord<PrincipalRecord>(KeySpace.Principal(AnnSeedTestSupport.Principal))!;
                    if (document is { Deleted: false } && document.Revision == vector.DocumentRevision
                        && database.Database.Authorization.CanReadRow(principal, document.Access))
                    {
                        CountRead(view, KeySpace.Partition(LineageKeySpace, database.Partition,
                            AnnSeedTestSupport.Collection, vector.Field, vector.DocumentId), counter);
                    }
                    return true;
                }, observer: counter.Add);
            return counter.Total;
        });

    private static void CountRead(IKeyValueView view, byte[] key, AnnSeedReadByteCounter counter)
        => view.ReadValue(key, static _ => { }, counter.Add);

    private sealed class AnnSeedReadByteCounter
    {
        internal long Total { get; private set; }
        internal void Add(long value) => Total = checked(Total + value);
    }

    [Test]
    public async Task RepeatedCaptureUnderOneSystemBudgetStopsAtRealDeadline()
    {
        const int MaxCaptureCalls = 1024;
        const long MaxCumulativeSourceBytes = 2_147_483_648;
        var limits = new DatabaseLimits
        {
            QueryDeadlineSeconds = 1,
            MaxQueryReadBytes = MaxCumulativeSourceBytes
        };
        using var database = AnnSeedTestSupport.Create(10_000, limits);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits), TimeProvider.System);
        var started = TimeProvider.System.GetTimestamp();
        var calls = 0;
        var successfulReadDeltasPositive = true;
        long failedCallBytes = 0;
        KeyLoadException? deadline = null;

        while (calls < MaxCaptureCalls && budget.ReadBytes < MaxCumulativeSourceBytes)
        {
            var before = budget.ReadBytes;
            try
            {
                _ = AnnSeedTestSupport.Capture(database, budget: budget);
                calls++;
                successfulReadDeltasPositive &= budget.ReadBytes - before > 0;
            }
            catch (KeyLoadException failure)
            {
                failedCallBytes = budget.ReadBytes - before;
                deadline = failure;
                break;
            }
        }

        await Assert.That(deadline).IsNotNull();
        await Assert.That(deadline!.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failedCallBytes).IsGreaterThan(0L);
        await Assert.That(successfulReadDeltasPositive).IsTrue();
        await Assert.That(calls).IsLessThan(MaxCaptureCalls);
        await Assert.That(budget.ReadBytes).IsLessThanOrEqualTo(MaxCumulativeSourceBytes);
        await Assert.That(TimeProvider.System.GetElapsedTime(started)).IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        var healthyBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits with { QueryDeadlineSeconds = 30 }));
        var healthy = AnnSeedTestSupport.Capture(database, budget: healthyBudget);
        await Assert.That(healthy.Records.Length).IsEqualTo(10_000);
    }
}
