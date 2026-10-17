using ZoneTree.Analytics;

namespace KeyLoad.Server;

internal static class OperationalAnalyticsQuery
{
    private const long EmptyTicks = 0;
    private const int NextHour = 1;
    internal static void Validate(DateTimeOffset from, DateTimeOffset to, DateTimeOffset now,
        OperationalAnalyticsOptions settings)
    {
        var nextHour = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero).AddHours(NextHour);
        if (from.Offset != TimeSpan.Zero || to.Offset != TimeSpan.Zero
            || from.Ticks % TimeSpan.TicksPerHour != EmptyTicks || to.Ticks % TimeSpan.TicksPerHour != EmptyTicks
            || from >= to || to - from > settings.Retention || from < now - settings.Retention || to > nextHour)
        {
            throw new ArgumentOutOfRangeException(nameof(from));
        }
    }

    internal static AnalyticsQuery Create(DateTimeOffset from, DateTimeOffset to)
        => new()
        {
            Site = OperationalAnalyticsProtocol.Site,
            Event = OperationalAnalyticsProtocol.Sample,
            Range = DateRange.Between(from, to),
            TimeZone = TimeZoneInfo.Utc,
            Interval = TimeInterval.Hour,
            Fill = FillMode.None,
            Source = QuerySource.Rollups,
            Metrics =
            {
                Metric.Events,
                Metric.Sum(OperationalAnalyticsProtocol.Committed),
                Metric.Sum(OperationalAnalyticsProtocol.Applied),
                Metric.Sum(OperationalAnalyticsProtocol.Backlog)
            }
        };
}
