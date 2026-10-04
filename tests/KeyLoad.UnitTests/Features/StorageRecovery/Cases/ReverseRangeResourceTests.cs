using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ReverseRangeResourceTests
{
    private const string Prefix = "p/";
    private const int PageLimit = 1;
    private const int MaximumRecords = 100;
    private const int MaximumRangeRecords = 100_000;

    [Test]
    public async Task AcRangeRev003ObserverChargesDescendingEntriesAndLiveLookahead()
    {
        using var fixture = new ScopedRangeStoreFixture();
        Seed(fixture.Store);
        var before = fixture.Store.GetReadDiagnostics();
        var observed = 0L;
        var names = new List<string>();

        var page = fixture.Store.Read(view => view.VisitReverseRange(Key(Prefix), PageLimit, (key, _) =>
        {
            names.Add(Encoding.UTF8.GetString(key));
            return true;
        }, observer: bytes => observed += bytes));
        var after = fixture.Store.GetReadDiagnostics();

        await Assert.That(names).IsEquivalentTo(new[] { "p/3" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(page.HasMore).IsTrue();
        await Assert.That(page.ReadBytes).IsEqualTo(16L);
        await Assert.That(observed).IsEqualTo(16L);
        await Assert.That(after.RangeBaselineEntries - before.RangeBaselineEntries).IsEqualTo(2L);
        await Assert.That(after.RangeLimitLookaheads - before.RangeLimitLookaheads).IsEqualTo(1L);
        await Assert.That(after.RangeExaminedBytes - before.RangeExaminedBytes).IsEqualTo(16L);
    }

    [Test]
    public async Task AcRangeRev003VisitorFalseStopsBeforeAdvancingAndPreCancellationExaminesNothing()
    {
        using var fixture = new ScopedRangeStoreFixture();
        Seed(fixture.Store);
        var before = fixture.Store.GetReadDiagnostics();
        var visits = 0;
        var stopped = fixture.Store.Read(view => view.VisitReverseRange(Key(Prefix), MaximumRecords, (_, _) =>
        {
            visits++;
            return false;
        }));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Store.Read(view =>
            view.VisitReverseRange(Key(Prefix), MaximumRecords, (_, _) => true, cancellationToken: cancelled.Token)));
        var after = fixture.Store.GetReadDiagnostics();

        await Assert.That(visits).IsEqualTo(1);
        await Assert.That(stopped.StoppedByVisitor).IsTrue();
        await Assert.That(stopped.HasMore).IsFalse();
        await Assert.That(after.RangeBaselineEntries - before.RangeBaselineEntries).IsEqualTo(1L);
    }

    [Test]
    public async Task AcRangeRev003CancellationAndObserverOrVisitorFaultReleaseCursorsAndStoreGate()
    {
        using var fixture = new ScopedRangeStoreFixture();
        Seed(fixture.Store);
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Store.Read(view =>
            view.VisitReverseRange(Key(Prefix), MaximumRecords, (_, _) => true,
                observer: _ => { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); },
                cancellationToken: cancellation.Token)));
        Assert.ThrowsExactly<ObserverFailureException>(() => fixture.Store.Read(view =>
            view.VisitReverseRange(Key(Prefix), MaximumRecords, (_, _) => true,
                observer: _ => throw new ObserverFailureException())));
        Assert.ThrowsExactly<VisitorFailureException>(() => fixture.Store.Read(view =>
            view.VisitReverseRange(Key(Prefix), MaximumRecords, (_, _) => throw new VisitorFailureException())));

        fixture.Store.Commit((tx, _) => { tx.Put(Key("healthy"), Key("yes")); return true; });
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(Key("healthy"))!))
            .IsEquivalentTo(Key("yes"));
        var followUp = new List<string>();
        fixture.Store.Read(view => view.VisitReverseRange(Key(Prefix), MaximumRecords, (key, _) =>
        {
            followUp.Add(Encoding.UTF8.GetString(key));
            return true;
        }));
        await Assert.That(followUp).IsEquivalentTo(new[] { "p/3", "p/2", "p/1" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcRangeRev003TransactionObserverChargesReplacementAndTombstoneWork()
    {
        using var fixture = new ScopedRangeStoreFixture();
        Seed(fixture.Store);
        var observed = 0L;
        var visits = new List<string>();

        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(Key("p/3"), Key("new"));
            tx.Delete(Key("p/2"));
            tx.Put(Key("p/4"), Key("new"));
            var result = tx.VisitReverseRange(Key(Prefix), MaximumRecords, (key, _) =>
            {
                visits.Add(Encoding.UTF8.GetString(key));
                return true;
            }, observer: bytes => observed += bytes);
            if (result.Records != 3 || result.HasMore)
            {
                throw new InvalidOperationException("The reverse transaction overlay was incomplete.");
            }
            return true;
        });

        await Assert.That(visits).IsEquivalentTo(new[] { "p/4", "p/3", "p/1" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(observed).IsEqualTo(39L);
    }

    [Test]
    public async Task AcRangeRev003InvalidCapsFailWithoutChangingCommittedData()
    {
        using var fixture = new ScopedRangeStoreFixture();
        Seed(fixture.Store);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view =>
            view.VisitReverseRange(Key(Prefix), 0, (_, _) => true)));
        var serverLimitFailure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view =>
            view.VisitReverseRange(Key(Prefix), MaximumRangeRecords + 1, (_, _) => true)));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(serverLimitFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(fixture.Store.Read(view => view.Scan(Key(Prefix), MaximumRecords).Records.Length)).IsEqualTo(3);
    }

    private static void Seed(ZoneTreeStore store) => store.Commit((tx, _) =>
    {
        foreach (var suffix in new[] { "1", "2", "3" })
        {
            tx.Put(Key(Prefix + suffix), Key("value"));
        }
        return true;
    });

    private static byte[] Key(string text) => Encoding.UTF8.GetBytes(text);
    private sealed class ObserverFailureException : Exception
    {
        public ObserverFailureException() { }
        public ObserverFailureException(string message) : base(message) { }
        public ObserverFailureException(string message, Exception innerException) : base(message, innerException) { }
    }

    private sealed class VisitorFailureException : Exception
    {
        public VisitorFailureException() { }
        public VisitorFailureException(string message) : base(message) { }
        public VisitorFailureException(string message, Exception innerException) : base(message, innerException) { }
    }
}
