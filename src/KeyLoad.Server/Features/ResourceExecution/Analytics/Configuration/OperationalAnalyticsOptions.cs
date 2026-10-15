namespace KeyLoad.Server;

[ConfigurationOptions]
internal sealed class OperationalAnalyticsOptions
{
    private const int MinimumCount = 1;
    private const int MutableItemsCeiling = 4_096;
    private const int DiskItemsCeiling = 16_384;
    private const int QueueCeiling = 64;
    private const int BufferedRowsCeiling = 128;
    private const int GroupsCeiling = 192;
    private const int BucketsCeiling = 168;
    private const int RowsCeiling = 192;
    private const int SampleSeconds = 10;
    private const int FlushSeconds = 2;
    private const int QuerySeconds = 2;
    private const int RetentionDays = 7;
    private const int RetentionCheckMinutes = 60;
    private static readonly TimeSpan RequiredSampleInterval = TimeSpan.FromSeconds(SampleSeconds);
    private static readonly TimeSpan MaximumFlushInterval = TimeSpan.FromSeconds(FlushSeconds);
    private static readonly TimeSpan MaximumQueryTimeout = TimeSpan.FromSeconds(QuerySeconds);
    private static readonly TimeSpan MaximumRetention = TimeSpan.FromDays(RetentionDays);
    private static readonly TimeSpan RequiredRetentionCheckInterval = TimeSpan.FromMinutes(RetentionCheckMinutes);
    public int StoreMutableItems { get; init; } = MutableItemsCeiling;
    public int StoreDiskItems { get; init; } = DiskItemsCeiling;
    public int QueueCapacity { get; init; } = QueueCeiling;
    public int BufferedRows { get; init; } = BufferedRowsCeiling;
    public int MaximumGroups { get; init; } = GroupsCeiling;
    public int MaximumBuckets { get; init; } = BucketsCeiling;
    public int MaximumRows { get; init; } = RowsCeiling;
    public int MaximumReportBytes { get; init; } = OperationalAnalyticsProtocol.MaximumReportBytes;
    public TimeSpan SampleInterval { get; init; } = RequiredSampleInterval;
    public TimeSpan FlushInterval { get; init; } = MaximumFlushInterval;
    public TimeSpan QueryTimeout { get; init; } = MaximumQueryTimeout;
    public TimeSpan Retention { get; init; } = MaximumRetention;
    public TimeSpan RetentionCheckInterval { get; init; } = RequiredRetentionCheckInterval;

    internal bool IsValid()
        => Positive(StoreMutableItems, MutableItemsCeiling) && Positive(StoreDiskItems, DiskItemsCeiling)
            && StoreDiskItems >= StoreMutableItems && Positive(QueueCapacity, QueueCeiling) && Positive(BufferedRows, BufferedRowsCeiling)
            && Positive(MaximumGroups, GroupsCeiling) && Positive(MaximumBuckets, BucketsCeiling)
            && Positive(MaximumRows, RowsCeiling)
            && Positive(MaximumReportBytes, OperationalAnalyticsProtocol.MaximumReportBytes)
            && SampleInterval == RequiredSampleInterval
            && Bounded(FlushInterval, MaximumFlushInterval)
            && Bounded(QueryTimeout, MaximumQueryTimeout)
            && Bounded(Retention, MaximumRetention)
            && RetentionCheckInterval == RequiredRetentionCheckInterval;

    private static bool Positive(int value, int maximum) => value >= MinimumCount && value <= maximum;
    private static bool Bounded(TimeSpan value, TimeSpan maximum) => value > TimeSpan.Zero && value <= maximum;
}
