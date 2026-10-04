using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ReadDiagnosticsTests
{
    private const string PointKey = "p/1";
    private const string OtherKey = "p/2";
    private const string MissingKey = "missing";
    private const string OldValue = "old";
    private const string NewValue = "new";

    [Test]
    public async Task AcMp011OwnedAndBorrowedPointsCountFoundMissingAndObserverRejectionOnce()
    {
        using var fixture = new StoreFixture();
        fixture.Store.Commit((tx, _) => { tx.Put(Key(PointKey), Key(OldValue)); return true; });
        var before = fixture.Store.GetReadDiagnostics();
        var found = fixture.Store.Read(view => view.ReadOwnedValue(Key(PointKey)));
        var missing = fixture.Store.Read(view => view.ReadOwnedValue(Key(MissingKey)));
        var borrowed = fixture.Store.Read(view => view.ReadValue(Key(PointKey), value =>
            AssertValue(value, OldValue)));
        var missingBorrowed = fixture.Store.Read(view => view.ReadValue(Key(MissingKey),
            _ => throw new InvalidOperationException()));
        var called = false;
        Assert.ThrowsExactly<ObserverRejectedException>(() => fixture.Store.Read(view =>
            view.ReadValue(Key(PointKey), _ => called = true, _ => throw new ObserverRejectedException())));
        var after = fixture.Store.GetReadDiagnostics();

        await Assert.That(found).IsEquivalentTo(Key(OldValue));
        await Assert.That(missing).IsNull();
        await Assert.That(borrowed).IsTrue();
        await Assert.That(missingBorrowed).IsFalse();
        await Assert.That(called).IsFalse();
        await Assert.That(after.SessionId).IsEqualTo(before.SessionId);
        await Assert.That(after.OwnedPointLookups - before.OwnedPointLookups).IsEqualTo(2L);
        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo(3L);
        await Assert.That(after.PointExaminedBytes - before.PointExaminedBytes)
            .IsEqualTo(3L * (PointKey.Length + OldValue.Length) + 2L * MissingKey.Length);
    }

    [Test]
    public async Task AcMp011StagedReplacementTombstoneAndFallbackCountEachLookupOnce()
    {
        using var fixture = new StoreFixture();
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(Key(PointKey), Key(OldValue));
            tx.Put(Key(OtherKey), Key(OldValue));
            return true;
        });
        var before = fixture.Store.GetReadDiagnostics();
        fixture.Store.Commit((tx, _) =>
        {
            AssertValue(tx.ReadOwnedValue(Key(PointKey))!, OldValue);
            tx.ReadValue(Key(PointKey), value => AssertValue(value, OldValue));
            tx.Put(Key(PointKey), Key(NewValue));
            AssertValue(tx.ReadOwnedValue(Key(PointKey))!, NewValue);
            tx.ReadValue(Key(PointKey), value => AssertValue(value, NewValue));
            tx.Delete(Key(OtherKey));
            if (tx.ReadOwnedValue(Key(OtherKey)) is not null || tx.ReadValue(Key(OtherKey), _ => throw new InvalidOperationException()))
            {
                throw new InvalidOperationException();
            }
            if (tx.ReadOwnedValue(Key(MissingKey)) is not null || tx.ReadValue(Key(MissingKey), _ => throw new InvalidOperationException()))
            {
                throw new InvalidOperationException();
            }
            return true;
        });
        var after = fixture.Store.GetReadDiagnostics();
        await Assert.That(after.OwnedPointLookups - before.OwnedPointLookups).IsEqualTo(4L);
        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo(4L);
        await Assert.That(after.PointExaminedBytes - before.PointExaminedBytes)
            .IsEqualTo(4L * (PointKey.Length + OldValue.Length) + 2L * OtherKey.Length + 2L * MissingKey.Length);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(Key(PointKey)))).IsEquivalentTo(Key(NewValue));
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(Key(OtherKey)))).IsNull();
    }

    [Test]
    public async Task AcMp011ReopenStartsNewDiagnosticsSessionWithoutChangingStoredValues()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-read-diagnostics-" + Guid.NewGuid().ToString("N"));
        try
        {
            Guid priorSession;
            using (var store = new ZoneTreeStore(new(directory)))
            {
                store.Commit((tx, _) => { tx.Put(Key(PointKey), Key(OldValue)); return true; });
                store.Read(view => view.ReadOwnedValue(Key(PointKey)));
                priorSession = store.GetReadDiagnostics().SessionId;
            }
            using var reopened = new ZoneTreeStore(new(directory));
            var current = reopened.GetReadDiagnostics();
            await Assert.That(current.SessionId).IsNotEqualTo(priorSession);
            await Assert.That(current.OwnedPointLookups).IsEqualTo(0L);
            await Assert.That(current.BorrowedPointLookups).IsEqualTo(0L);
            await Assert.That(current.PointExaminedBytes).IsEqualTo(0L);
            await Assert.That(reopened.Read(view => view.ReadOwnedValue(Key(PointKey)))).IsEquivalentTo(Key(OldValue));
        }
        finally
        {
            System.IO.Directory.Delete(directory, true);
        }
    }

    private static byte[] Key(string value) => Encoding.UTF8.GetBytes(value);
    private static void AssertValue(ReadOnlySpan<byte> actual, string expected)
    {
        if (!actual.SequenceEqual(Key(expected)))
        {
            throw new InvalidOperationException();
        }
    }

    private sealed class ObserverRejectedException : Exception
    {
        public ObserverRejectedException() { }
        public ObserverRejectedException(string message) : base(message) { }
        public ObserverRejectedException(string message, Exception innerException) : base(message, innerException) { }
    }

    private sealed class StoreFixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "keyload-read-diagnostics-" + Guid.NewGuid().ToString("N"));
        public ZoneTreeStore Store { get; }
        public StoreFixture() => Store = new(new(Directory));
        public void Dispose()
        {
            Store.Dispose();
            System.IO.Directory.Delete(Directory, true);
        }
    }
}
