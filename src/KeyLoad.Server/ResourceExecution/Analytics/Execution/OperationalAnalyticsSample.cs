using ZoneTree.Analytics;

namespace KeyLoad.Server;

internal static class OperationalAnalyticsSample
{
    private const long EmptyPosition = 0;
    internal static AnalyticsEvent Create(ReplicaNodeState snapshot, DateTimeOffset observed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.MaterializedPosition < EmptyPosition || snapshot.CommittedIndex < snapshot.MaterializedPosition
            || snapshot.LastIndex < snapshot.CommittedIndex || snapshot.LastIndex > OperationalAnalyticsProtocol.MaximumExactInteger)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshot));
        }
        return new(OperationalAnalyticsProtocol.Sample)
        {
            Timestamp = observed,
            Values =
            {
                [OperationalAnalyticsProtocol.Committed] = snapshot.CommittedIndex,
                [OperationalAnalyticsProtocol.Applied] = snapshot.MaterializedPosition,
                [OperationalAnalyticsProtocol.Backlog] = checked(snapshot.CommittedIndex - snapshot.MaterializedPosition)
            }
        };
    }
}
