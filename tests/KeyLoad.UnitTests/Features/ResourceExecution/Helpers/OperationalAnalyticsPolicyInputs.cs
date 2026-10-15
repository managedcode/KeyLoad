using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class OperationalAnalyticsPolicyInputs
{
    internal const int ExpectedRefusals = 42;
    private const int NegativeCount = -1;
    private const int UnsetCount = 0;
    private const int AboveMutableItems = 4_097;
    private const int AboveDiskItems = 16_385;
    private const int AboveQueue = 65;
    private const int AboveBufferedRows = 129;
    private const int AboveGroups = 193;
    private const int AboveBuckets = 169;
    private const int AboveRows = 193;
    private const int AboveReportBytes = 65_537;
    private const int MutableItems = 4_096;
    private const int DiskBelowMutable = 4_095;
    private const long NegativeTick = -1;
    private const long FollowingTick = 1;
    private const int MaximumOperationSeconds = 2;
    private const int RetentionDays = 7;
    private const int ShortSampleSeconds = 9;
    private const int LongSampleSeconds = 11;
    private const int ShortCheckMinutes = 59;
    private const int LongCheckMinutes = 61;

    internal static IEnumerable<OperationalAnalyticsOptions> Counts()
    {
        foreach (var value in new[] { NegativeCount, UnsetCount, AboveMutableItems })
        {
            yield return new()
            {
                StoreMutableItems = value
            };
        }
        foreach (var value in new[] { NegativeCount, UnsetCount, AboveDiskItems })
        {
            yield return new()
            {
                StoreDiskItems = value
            };
        }
        foreach (var value in new[] { NegativeCount, UnsetCount, AboveQueue })
        {
            yield return new()
            {
                QueueCapacity = value
            };
        }
        foreach (var value in new[] { NegativeCount, UnsetCount, AboveBufferedRows })
        {
            yield return new()
            {
                BufferedRows = value
            };
        }
        foreach (var value in new[] { NegativeCount, UnsetCount, AboveGroups })
        {
            yield return new()
            {
                MaximumGroups = value
            };
        }
        foreach (var value in new[] { NegativeCount, UnsetCount, AboveBuckets })
        {
            yield return new()
            {
                MaximumBuckets = value
            };
        }
        foreach (var value in new[] { NegativeCount, UnsetCount, AboveRows })
        {
            yield return new()
            {
                MaximumRows = value
            };
        }
        foreach (var value in new[] { NegativeCount, UnsetCount, AboveReportBytes })
        {
            yield return new()
            {
                MaximumReportBytes = value
            };
        }
    }

    internal static IEnumerable<OperationalAnalyticsOptions> Times()
    {
        foreach (var value in new[] { TimeSpan.FromTicks(NegativeTick), TimeSpan.Zero, TimeSpan.FromSeconds(MaximumOperationSeconds).Add(TimeSpan.FromTicks(FollowingTick)) })
        {
            yield return new()
            {
                FlushInterval = value
            };
        }
        foreach (var value in new[] { TimeSpan.FromTicks(NegativeTick), TimeSpan.Zero, TimeSpan.FromSeconds(MaximumOperationSeconds).Add(TimeSpan.FromTicks(FollowingTick)) })
        {
            yield return new()
            {
                QueryTimeout = value
            };
        }
        foreach (var value in new[] { TimeSpan.FromTicks(NegativeTick), TimeSpan.Zero, TimeSpan.FromDays(RetentionDays).Add(TimeSpan.FromTicks(FollowingTick)) })
        {
            yield return new()
            {
                Retention = value
            };
        }
        foreach (var value in new[] { TimeSpan.FromTicks(NegativeTick), TimeSpan.Zero, TimeSpan.FromSeconds(ShortSampleSeconds), TimeSpan.FromSeconds(LongSampleSeconds) })
        {
            yield return new()
            {
                SampleInterval = value
            };
        }
        foreach (var value in new[] { TimeSpan.FromTicks(NegativeTick), TimeSpan.Zero, TimeSpan.FromMinutes(ShortCheckMinutes), TimeSpan.FromMinutes(LongCheckMinutes) })
        {
            yield return new()
            {
                RetentionCheckInterval = value
            };
        }
    }

    internal static OperationalAnalyticsOptions DiskRelation() => new()
    {
        StoreMutableItems = MutableItems,
        StoreDiskItems = DiskBelowMutable
    };
}
