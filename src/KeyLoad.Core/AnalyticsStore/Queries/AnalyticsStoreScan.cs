using System.Collections.Immutable;
using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.AnalyticsStore;

internal static class AnalyticsStoreScan
{
    private const long AbsentRevision = 0;
    internal static ImmutableArray<AnalyticsEventRecord> Read(DatabaseEngine database, IKeyValueView raw,
        AnalyticsStoreReadScope scope, PartitionRef partition, string store, DateTimeOffset from,
        DateTimeOffset to, ReadExecutionBudget work, AnalyticsDimensionMatch? match = null)
    {
        var rows = new Dictionary<string, AnalyticsEventRecord>(StringComparer.Ordinal);
        var timestamps = AnalyticsStoreKeys.Timestamps(partition, store);
        var events = AnalyticsStoreKeys.Events(partition, store);
        var decoder = new ReadExecutionRecordReader<AnalyticsStoredEvent>(work, raw);
        _ = work.VisitAll(raw, timestamps,
            (key, value) =>
            {
                work.Check();
                var id = OrleansSerialization.Deserialize<string>(value);
                if (string.IsNullOrEmpty(id))
                {
                    throw Errors.Fail(ErrorCode.Corruption, AnalyticsStoreValidation.Invalid);
                }
                var stored = decoder.Read(AnalyticsStoreKeys.Event(events, id));
                var row = stored?.Event;
                if (row is null || row.Revision <= AbsentRevision || row.Id != id)
                {
                    throw Errors.Fail(ErrorCode.Corruption, AnalyticsStoreValidation.Invalid);
                }
                // Direct resource writes can expose an older index entry while replacing its canonical row.
                var current = !stored!.Deleted
                    && AnalyticsStoreKeys.MatchesTimestamp(key, timestamps, row.Timestamp, id);
                if (!current && scope.Resource.Transactional)
                {
                    throw Errors.Fail(ErrorCode.Corruption, AnalyticsStoreValidation.Invalid);
                }
                if (current && row.Timestamp >= from && row.Timestamp < to
                    && database.Authorization.CanReadRow(scope.Principal, row.RowAccess ?? new())
                    && (match is null || string.Equals(row.Dimensions[match.Dimension], match.Value, StringComparison.Ordinal)))
                {
                    rows[id] = row;
                }

                return true;
            }, afterKey: AnalyticsStoreKeys.TimestampBoundary(partition, store, from),
            untilKey: AnalyticsStoreKeys.TimestampBoundary(partition, store, to));
        return [.. rows.Values.OrderBy(row => row.Id, StringComparer.Ordinal)];
    }
}
