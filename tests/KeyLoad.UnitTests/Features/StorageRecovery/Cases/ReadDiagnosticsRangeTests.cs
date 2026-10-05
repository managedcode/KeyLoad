using System.Globalization;
using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ReadDiagnosticsRangeTests
{
    private const string Prefix = "p/";
    private const string OldValue = "old";
    private const string NewValue = "new";
    private const int PageLimit = 2;
    private const int FullLimit = 10;
    private const int MaximumExaminedRecords = 100_000;
    private const string TombstoneFormat = "D6";

    [Test]
    public async Task AcMp011MergedRangeCountsOverwrittenBaselineTombstonePrefetchAndLiveLookahead()
    {
        using var fixture = new StoreFixture();
        Seed(fixture.Store);
        var before = fixture.Store.GetReadDiagnostics();
        var names = new List<string>();
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(Key("p/2"), Key(NewValue));
            tx.Delete(Key("p/3"));
            tx.Put(Key("p/4"), Key(NewValue));
            var result = tx.VisitRange(Key(Prefix), PageLimit, (key, _) =>
            {
                names.Add(Encoding.UTF8.GetString(key));
                return true;
            });
            if (result.Records != PageLimit || !result.HasMore || result.ReadBytes != 33)
            {
                throw new InvalidOperationException();
            }
            return true;
        });
        var after = fixture.Store.GetReadDiagnostics();
        await Assert.That(names).IsEquivalentTo(new[] { "p/1", "p/2" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(after.RangeVisitAttempts - before.RangeVisitAttempts).IsEqualTo(1L);
        await Assert.That(after.RangeBaselineEntries - before.RangeBaselineEntries).IsEqualTo(3L);
        await Assert.That(after.RangeStagedEntries - before.RangeStagedEntries).IsEqualTo(3L);
        await Assert.That(after.RangeStagedTombstones - before.RangeStagedTombstones).IsEqualTo(1L);
        await Assert.That(after.RangeLimitLookaheads - before.RangeLimitLookaheads).IsEqualTo(1L);
        await Assert.That(after.RangeExaminedBytes - before.RangeExaminedBytes).IsEqualTo(33L);
    }

    [Test]
    public async Task AcMp011BoundedRangeAndOwnedScanEachCountOneAttempt()
    {
        using var fixture = new StoreFixture();
        Seed(fixture.Store);
        var before = fixture.Store.GetReadDiagnostics();
        var bounded = fixture.Store.Read(view => view.VisitRange(Key(Prefix), FullLimit, (_, _) => true,
            afterKey: Key("p/1"), untilKey: Key("p/3")));
        var afterBounded = fixture.Store.GetReadDiagnostics();
        var page = fixture.Store.Read(view => view.Scan(Key(Prefix), FullLimit));
        var afterPage = fixture.Store.GetReadDiagnostics();
        await Assert.That(bounded.Records).IsEqualTo(1);
        await Assert.That(bounded.ReadBytes).IsEqualTo(6L);
        await Assert.That(afterBounded.RangeVisitAttempts - before.RangeVisitAttempts).IsEqualTo(1L);
        await Assert.That(afterBounded.RangeBaselineEntries - before.RangeBaselineEntries).IsEqualTo(1L);
        await Assert.That(afterBounded.RangeExaminedBytes - before.RangeExaminedBytes).IsEqualTo(6L);
        await Assert.That(page.Records.Length).IsEqualTo(3);
        await Assert.That(afterPage.RangeVisitAttempts - afterBounded.RangeVisitAttempts).IsEqualTo(1L);
        await Assert.That(afterPage.RangeBaselineEntries - afterBounded.RangeBaselineEntries).IsEqualTo(3L);
        await Assert.That(afterPage.RangeExaminedBytes - afterBounded.RangeExaminedBytes).IsEqualTo(18L);
    }

    [Test]
    public async Task AcMp011InvalidAndPreCancelledRangesCountAttemptsWithoutExaminingData()
    {
        using var fixture = new StoreFixture();
        Seed(fixture.Store);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var before = fixture.Store.GetReadDiagnostics();
        Assert.ThrowsExactly<ArgumentNullException>(() => fixture.Store.Read(view =>
            view.VisitRange(null!, FullLimit, (_, _) => true)));
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Store.Read(view =>
            view.VisitRange(Key(Prefix), FullLimit, (_, _) => true, cancellationToken: cancellation.Token)));
        var after = fixture.Store.GetReadDiagnostics();
        await Assert.That(after.RangeVisitAttempts - before.RangeVisitAttempts).IsEqualTo(2L);
        await Assert.That(after.RangeBaselineEntries - before.RangeBaselineEntries).IsEqualTo(0L);
        await Assert.That(after.RangeStagedEntries - before.RangeStagedEntries).IsEqualTo(0L);
        await Assert.That(after.RangeExaminedBytes - before.RangeExaminedBytes).IsEqualTo(0L);
    }

    [Test]
    public async Task AcMp011FetchedObserverCancellationAndRejectionRetainWorkAndLeaveStoreHealthy()
    {
        using var fixture = new StoreFixture();
        Seed(fixture.Store);
        using var cancellation = new CancellationTokenSource();
        var before = fixture.Store.GetReadDiagnostics();
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Store.Read(view =>
            view.VisitRange(Key(Prefix), FullLimit, (_, _) => true, observer: _ =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }, cancellationToken: cancellation.Token)));
        var afterCancellation = fixture.Store.GetReadDiagnostics();
        Assert.ThrowsExactly<ObserverRejectedException>(() => fixture.Store.Read(view =>
            view.VisitRange(Key(Prefix), FullLimit, (_, _) => true,
                observer: _ => throw new ObserverRejectedException())));
        var afterRejection = fixture.Store.GetReadDiagnostics();
        await Assert.That(afterCancellation.RangeBaselineEntries - before.RangeBaselineEntries).IsEqualTo(1L);
        await Assert.That(afterCancellation.RangeExaminedBytes - before.RangeExaminedBytes).IsEqualTo(6L);
        await Assert.That(afterRejection.RangeBaselineEntries - afterCancellation.RangeBaselineEntries).IsEqualTo(1L);
        await Assert.That(afterRejection.RangeExaminedBytes - afterCancellation.RangeExaminedBytes).IsEqualTo(6L);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(Key("p/1")))).IsEquivalentTo(Key(OldValue));
    }

    [Test]
    public async Task AcMp011ExaminedRecordCapKeepsRejectedTombstoneAndFollowingOperationWorks()
    {
        using var fixture = new StoreFixture();
        var before = fixture.Store.GetReadDiagnostics();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Commit((tx, _) =>
        {
            for (var index = 0; index <= MaximumExaminedRecords; index++)
            {
                tx.Delete(Key(Prefix + index.ToString(TombstoneFormat, CultureInfo.InvariantCulture)));
            }
            tx.VisitRange(Key(Prefix), MaximumExaminedRecords, (_, _) => throw new InvalidOperationException());
            return true;
        }));
        var after = fixture.Store.GetReadDiagnostics();
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(after.RangeVisitAttempts - before.RangeVisitAttempts).IsEqualTo(1L);
        await Assert.That(after.RangeStagedEntries - before.RangeStagedEntries).IsEqualTo(MaximumExaminedRecords + 1L);
        await Assert.That(after.RangeStagedTombstones - before.RangeStagedTombstones).IsEqualTo(MaximumExaminedRecords + 1L);
        await Assert.That(after.RangeExaminedBytes - before.RangeExaminedBytes)
            .IsEqualTo((MaximumExaminedRecords + 1L) * 8L);
        fixture.Store.Commit((tx, _) => { tx.Put(Key("healthy"), Key(NewValue)); return true; });
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(Key("healthy")))).IsEquivalentTo(Key(NewValue));
    }

    private static void Seed(ZoneTreeStore store) => store.Commit((tx, _) =>
    {
        foreach (var name in new[] { "p/1", "p/2", "p/3" })
        {
            tx.Put(Key(name), Key(OldValue));
        }
        return true;
    });

    private static byte[] Key(string value) => Encoding.UTF8.GetBytes(value);
    private sealed class ObserverRejectedException : Exception
    {
        public ObserverRejectedException() { }
        public ObserverRejectedException(string message) : base(message) { }
        public ObserverRejectedException(string message, Exception innerException) : base(message, innerException) { }
    }

    private sealed class StoreFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "keyload-read-diagnostics-" + Guid.NewGuid().ToString("N"));
        public ZoneTreeStore Store { get; }
        public StoreFixture() => Store = new(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        public void Dispose()
        {
            Store.Dispose();
            Directory.Delete(directory, true);
        }
    }
}
