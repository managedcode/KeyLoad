using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeRangeWork(ZoneTreeReadCounters counters, StorageReadObserver? observer,
    int maximumRecords, long maximumWorkBytes, CancellationToken cancellationToken)
{
    private const int NoMarkerBytes = 0;

    private long providerBytes;
    private int examinedEntries;
    internal long ReadBytes { get; private set; }
    internal void Check() => cancellationToken.ThrowIfCancellationRequested();
    internal void CountLookahead() => counters.Lookahead();
    internal void ChargeBaseline(long bytes) => Charge(bytes, true, false);
    internal void ChargeStaged(long bytes, bool tombstone) => Charge(bytes, false, tombstone);

    private void Charge(long bytes, bool hasMarker, bool tombstone)
    {
        counters.RangeEntry(bytes, baseline: hasMarker, tombstone);
        Check();
        if (++examinedEntries > maximumRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ExhaustedRangeRecordsMessage);
        }

        var providerCharge = bytes + (hasMarker ? StorageValueHeaderBytes : NoMarkerBytes);
        if (providerCharge > maximumWorkBytes - providerBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ExhaustedRangeBytesMessage);
        }

        providerBytes += providerCharge;
        ReadBytes += bytes;
        observer?.Invoke(bytes);
    }
}
