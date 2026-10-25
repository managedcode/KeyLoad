using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Visits a complete key range in pages sized by the storage provider's own range ceiling.</summary>
internal static class StorageRangePages
{
    private const int FirstRecord = 1;

    /// <summary>Visits one provider-sized page of a range.</summary>
    /// <param name="maxRecords">The provider's accepted record count for one range operation.</param>
    /// <param name="visitor">Read-only callback for the page records.</param>
    /// <param name="afterKey">Exclusive lower bound continuing after the previous page.</param>
    /// <returns>The page scan result.</returns>
    internal delegate StorageScanResult PageVisit(int maxRecords, StorageRecordVisitor visitor, byte[]? afterKey);

    /// <summary>Visits every live record after the optional bound until the range ends or the visitor stops.</summary>
    /// <param name="pageRecords">The provider's accepted record count for one range operation.</param>
    /// <param name="visit">Performs one page visit against the caller's gate-scoped view.</param>
    /// <param name="visitor">Read-only callback; returning false stops the traversal.</param>
    /// <param name="afterKey">Optional exclusive lower bound.</param>
    /// <returns>Delivered records and examined bytes across all pages; HasMore is set only when the visitor stopped early.</returns>
    internal static StorageScanResult VisitAll(int pageRecords, PageVisit visit, StorageRecordVisitor visitor,
        byte[]? afterKey = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageRecords, FirstRecord);
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(visitor);
        var records = 0;
        var bytes = 0L;
        var after = afterKey;
        while (true)
        {
            var delivered = 0;
            byte[]? last = null;
            var page = visit(pageRecords, (key, value) =>
            {
                if (++delivered == pageRecords)
                {
                    last = key.ToArray();
                }
                return visitor(key, value);
            }, after);
            records = checked(records + page.Records);
            bytes = checked(bytes + page.ReadBytes);
            if (page.StoppedByVisitor || !page.HasMore || last is null)
            {
                return new(records, page.HasMore, page.StoppedByVisitor, bytes);
            }
            after = last;
        }
    }

    /// <summary>Visits every live record of a gate-scoped view range in provider-sized pages.</summary>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="visitor">Read-only callback; returning false stops the traversal.</param>
    /// <param name="afterKey">Optional exclusive lower bound.</param>
    /// <param name="untilKey">Optional exclusive upper bound.</param>
    /// <returns>Delivered records and examined bytes across all pages.</returns>
    internal static StorageScanResult VisitAll(this IKeyValueView view, byte[] prefix, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        return VisitAll(view.MaximumRangeRecords,
            (maxRecords, pageVisitor, after) => view.VisitRange(prefix, maxRecords, pageVisitor, after, untilKey),
            visitor, afterKey);
    }

    /// <summary>Copies every live record of a gate-scoped view range in provider-sized pages.</summary>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="afterKey">Optional exclusive lower bound.</param>
    /// <returns>Independent key/value copies of the complete range.</returns>
    internal static KeyValueRecord[] ScanAll(this IKeyValueView view, byte[] prefix, byte[]? afterKey = null)
    {
        var records = new List<KeyValueRecord>();
        _ = view.VisitAll(prefix, (key, value) =>
        {
            records.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, afterKey);
        return [.. records];
    }
}
