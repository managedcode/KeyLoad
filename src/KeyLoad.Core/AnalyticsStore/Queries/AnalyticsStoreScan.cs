using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core.AnalyticsStore;

internal static class AnalyticsStoreScan
{
    private const long AbsentRevision = 0;
    internal static ImmutableArray<AnalyticsEventRecord> Read(DatabaseEngine database, IKeyValueView raw,
        AnalyticsStoreReadScope scope, PartitionRef partition, string store, DateTimeOffset from,
        DateTimeOffset to, ReadExecutionBudget work)
    {
        var rows = ImmutableArray.CreateBuilder<AnalyticsEventRecord>();
        var scan = work.VisitRange(raw, AnalyticsStoreKeys.Events(partition, store), database.Limits.MaxScanRecords,
            (key, value) =>
            {
                work.Check();
                var stored = OrleansSerialization.Deserialize<AnalyticsStoredEvent>(value);
                var row = stored.Event;
                if (row is null || row.Revision <= AbsentRevision || !key.SequenceEqual(AnalyticsStoreKeys.Event(partition, store, row.Id)))
                {
                    throw Errors.Fail(ErrorCode.Corruption, AnalyticsStoreValidation.Invalid);
                }

                if (!stored.Deleted && row.Timestamp >= from && row.Timestamp < to
                    && database.Authorization.CanReadRow(scope.Principal, row.RowAccess ?? new()))
                {
                    rows.Add(row);
                }

                return true;
            });
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnalyticsStoreValidation.Budget);
        }

        return [.. rows.OrderBy(row => row.Id, StringComparer.Ordinal)];
    }
}
