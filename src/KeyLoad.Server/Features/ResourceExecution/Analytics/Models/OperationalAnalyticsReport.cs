using System.Collections.Immutable;

namespace KeyLoad.Server;

internal sealed record OperationalAnalyticsRow(DateTimeOffset Hour, double Samples,
    double CommittedSum, double AppliedSum, double BacklogSum);
internal sealed record OperationalAnalyticsCounters(long Accepted, long Processed, long Dropped,
    long Rejected, long Errors, long QueueLength);
internal sealed record OperationalAnalyticsReport(DateTimeOffset From, DateTimeOffset To,
    bool NativePlanComplete, ImmutableArray<OperationalAnalyticsRow> Rows, OperationalAnalyticsCounters Counters);
