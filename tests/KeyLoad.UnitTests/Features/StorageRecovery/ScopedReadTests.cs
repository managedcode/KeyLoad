using System.Runtime.InteropServices;
using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ScopedReadTests
{
    private const string SigningKeyDirectoryPrefix = "keyload-signing-key-";
    private const string ScanKey = "scan/key";
    private const string ScanPrefix = "scan/";
    private const string ScanValue = "scan-value";
    private const char ChangedBufferCharacter = 'x';
    private const int SigningKeyLength = 32;

    [Test]
    public async Task AcRoc004ScopedPointChargesBeforeBorrowAndKeepsOwnedRead()
    {
        using var fixture = new ScopedRangeStoreFixture();
        var key = Key("point");
        var value = new byte[1_048_576];
        fixture.Store.Commit((tx, _) => { tx.Put(key, value); return true; });
        var callback = false;
        var observed = 0L;
        await Assert.That(Assert.ThrowsExactly<BudgetRejectedException>(() => fixture.Store.Read(view =>
            view.ReadValue(key, _ => callback = true, bytes =>
            {
                observed = bytes;
                throw new BudgetRejectedException();
            })))).IsNotNull();
        await Assert.That(callback).IsFalse();
        await Assert.That(observed).IsEqualTo(key.Length + value.Length);
        var missing = Key("missing");
        var missingBytes = 0L;
        var found = fixture.Store.Read(view => view.ReadValue(missing, _ => callback = true, bytes => missingBytes = bytes));
        await Assert.That(found).IsFalse();
        await Assert.That(missingBytes).IsEqualTo(missing.Length);
        var owned = fixture.Store.Read(view => view.ReadOwnedValue(key)!);
        owned[0] = 42;
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(key)![0])).IsEqualTo((byte)0);
    }

    [Test]
    public async Task AcRoc003ScanReturnsImmutablePageWithIndependentOwnedBuffers()
    {
        using var fixture = new ScopedRangeStoreFixture();
        var key = Key(ScanKey);
        var value = Key(ScanValue);
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, value);
            return true;
        });

        var page = fixture.Store.Read(view => view.Scan(Key(ScanPrefix), 10));
        await Assert.That(page.Records.Length).IsEqualTo(1);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(MemoryMarshal.TryGetArray(page.Records[0].Key, out var keyBuffer)).IsTrue();
        await Assert.That(MemoryMarshal.TryGetArray(page.Records[0].Value, out var valueBuffer)).IsTrue();
        keyBuffer.Array![keyBuffer.Offset] = (byte)ChangedBufferCharacter;
        valueBuffer.Array![valueBuffer.Offset] = (byte)ChangedBufferCharacter;

        var stored = fixture.Store.Read(view => view.Scan(Key(ScanPrefix), 10));
        await Assert.That(stored.Records[0].Key.Span.SequenceEqual(key)).IsTrue();
        await Assert.That(stored.Records[0].Value.Span.SequenceEqual(value)).IsTrue();
    }

    [Test]
    public async Task AcRoc003StoreIdentityDoesNotAliasCallerSigningKeyAndReopensWithOriginalBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), SigningKeyDirectoryPrefix + Guid.NewGuid().ToString("N"));
        var callerKey = Enumerable.Range(1, SigningKeyLength).Select(static value => (byte)value).ToArray();
        var expectedKey = callerKey.ToArray();
        try
        {
            using (var store = new ZoneTreeStore(new(directory) { SigningKey = callerKey.AsMemory() }))
            {
                Array.Fill(callerKey, (byte)0);
                await Assert.That(store.Identity.SigningKey.Span.SequenceEqual(expectedKey)).IsTrue();
            }

            using var reopened = new ZoneTreeStore(new(directory) { SigningKey = expectedKey });
            await Assert.That(reopened.Identity.SigningKey.Span.SequenceEqual(expectedKey)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Test]
    public async Task AcMp002CancellationAndVisitorFailureLeaveStoreUsable()
    {
        using var fixture = new ScopedRangeStoreFixture();
        fixture.Store.Commit((tx, _) => { tx.Put(Key("a/1"), Key("one")); return true; });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.That(() => fixture.Store.Read(view => view.VisitRange(Key("a/"), 10, (_, _) => true,
            cancellationToken: cancellation.Token))).Throws<OperationCanceledException>();
        await Assert.That(() => fixture.Store.Read(view => view.VisitRange(Key("a/"), 10,
            (_, _) => throw new BudgetRejectedException()))).Throws<BudgetRejectedException>();
        fixture.Store.Commit((tx, _) => { tx.Put(Key("a/2"), Key("two")); return true; });
        await Assert.That(fixture.Store.Read(view => view.Scan(Key("a/"), 10).Records.Length)).IsEqualTo(2);
    }

    private static byte[] Key(string text) => Encoding.UTF8.GetBytes(text);

    private sealed class BudgetRejectedException : Exception
    {
        public BudgetRejectedException() { }
        public BudgetRejectedException(string message) : base(message) { }
        public BudgetRejectedException(string message, Exception innerException) : base(message, innerException) { }
    }

}
