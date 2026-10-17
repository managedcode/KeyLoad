namespace KeyLoad.Server.Features.ClusterRouting;

[ConfigurationOptions]
internal sealed class TenantPlacementExecutionOptions
{
    internal const string SectionName = "KeyLoad:TenantPlacementExecution";
    internal const int DefaultActive = 2;
    internal const int DefaultDescriptorBufferBytes = 4096;
    private const int MinimumPositiveLimit = 1;
    private const long MinimumAccountingBytes = 1L;
    internal const int DefaultPending = 16;
    internal const int DefaultMaximumPlans = 64;
    internal const int DefaultMaximumAccountingRecords = 65_536;
    internal const long DefaultMaximumAccountingBytes = 64L * 1024 * 1024;
    internal const int DefaultSampleSeconds = 5;
    internal const int DefaultFreshnessSeconds = 15;

    /// <summary>Gets or sets the explicit placement capability admission.</summary>
    public bool Enabled
    {
        get; set;
    }
    /// <summary>Gets or sets active apply calls on this Control-owner silo.</summary>
    public int MaximumActive { get; set; } = DefaultActive;
    /// <summary>Gets or sets waiting apply calls on this Control-owner silo.</summary>
    public int MaximumPending { get; set; } = DefaultPending;
    /// <summary>Gets or sets complete whole partitions in one plan.</summary>
    public int MaximumPlanPartitions { get; set; } = DefaultMaximumPlans;
    /// <summary>Gets or sets examined accounting records per private owner query.</summary>
    public int MaximumAccountingRecords { get; set; } = DefaultMaximumAccountingRecords;
    /// <summary>Gets or sets examined bytes per private owner query.</summary>
    public long MaximumAccountingBytes { get; set; } = DefaultMaximumAccountingBytes;
    /// <summary>Gets or sets the original sample interval.</summary>
    public int SampleIntervalSeconds { get; set; } = DefaultSampleSeconds;
    /// <summary>Gets or sets the original sample freshness limit.</summary>
    public int FreshnessSeconds { get; set; } = DefaultFreshnessSeconds;

    /// <summary>Gets or sets the bounded native descriptor file buffer size.</summary>
    public int DescriptorBufferBytes { get; set; } = DefaultDescriptorBufferBytes;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(DescriptorBufferBytes, MinimumPositiveLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(DescriptorBufferBytes, DefaultDescriptorBufferBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumActive, MinimumPositiveLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumActive, DefaultActive);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumPending);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumPending, DefaultPending);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumPlanPartitions, MinimumPositiveLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumPlanPartitions, DefaultMaximumPlans);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumAccountingRecords, MinimumPositiveLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumAccountingRecords, DefaultMaximumAccountingRecords);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumAccountingBytes, MinimumAccountingBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumAccountingBytes, DefaultMaximumAccountingBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(SampleIntervalSeconds, MinimumPositiveLimit);
        ArgumentOutOfRangeException.ThrowIfLessThan(FreshnessSeconds, SampleIntervalSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(FreshnessSeconds, DefaultFreshnessSeconds);
    }
}
