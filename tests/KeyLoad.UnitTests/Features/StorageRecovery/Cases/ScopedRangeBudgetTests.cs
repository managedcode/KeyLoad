using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ScopedRangeBudgetTests
{
    private const int LargeValueBytes = 1_048_576;
    private const string DocumentPrefix = "document/";

    [Test]
    public async Task AcMp002RangeObserverRejectsBeforeVisitorAndOwnedPageCopy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-scoped-budget-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var key = Encoding.UTF8.GetBytes(DocumentPrefix + "large");
            store.Commit((tx, _) => { tx.Put(key, new byte[LargeValueBytes]); return true; });
            var visits = 0;
            var observed = 0L;
            await Assert.That(Assert.ThrowsExactly<RangeBudgetRejectedException>(() => store.Read(view =>
                view.VisitRange(Encoding.UTF8.GetBytes(DocumentPrefix), 10, (_, _) =>
                {
                    visits++;
                    return true;
                }, observer: bytes =>
                {
                    observed += bytes;
                    throw new RangeBudgetRejectedException();
                })))).IsNotNull();
            await Assert.That(visits).IsEqualTo(0);
            await Assert.That(observed).IsEqualTo(key.Length + (long)LargeValueBytes);
            await Assert.That(store.Read(view => view.ReadOwnedValue(key)!.Length)).IsEqualTo(LargeValueBytes);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class RangeBudgetRejectedException : Exception
    {
        public RangeBudgetRejectedException() { }
        public RangeBudgetRejectedException(string message) : base(message) { }
        public RangeBudgetRejectedException(string message, Exception innerException) : base(message, innerException) { }
    }
}
