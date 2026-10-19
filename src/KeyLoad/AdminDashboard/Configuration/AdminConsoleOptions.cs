namespace KeyLoad.Server;

/// <summary>Bounds circuit-owned administration observation and data pages.</summary>
[ConfigurationOptions]
internal sealed class AdminConsoleOptions
{
    internal const string SectionName = "KeyLoad:AdminConsole";
    internal const string Invalid = "Administration console limits must be positive and within their accepted bounds.";
    private const int DefaultPollSeconds = 5;
    private const int MaximumRequestSeconds = 30;
    private const int MaximumHistory = 120;
    private const int MaximumRows = 25;
    private const int Minimum = 1;
    private static readonly TimeSpan RequestCeiling = TimeSpan.FromSeconds(MaximumRequestSeconds);
    private static readonly TimeSpan PollFloor = TimeSpan.FromSeconds(Minimum);
    /// <summary>Gets or sets the automatic observation cadence while the tab is visible.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(DefaultPollSeconds);
    /// <summary>Gets or sets the deadline for one authorized read.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(MaximumRequestSeconds);
    /// <summary>Gets or sets the maximum retained observation intervals.</summary>
    public int HistoryLimit { get; set; } = MaximumHistory;
    /// <summary>Gets or sets the bounded document page size.</summary>
    public int RowLimit { get; set; } = MaximumRows;
    internal bool IsValid() => PollInterval >= PollFloor && PollInterval <= RequestCeiling
        && RequestTimeout > TimeSpan.Zero && RequestTimeout <= RequestCeiling
        && HistoryLimit is >= Minimum and <= MaximumHistory && RowLimit is >= Minimum and <= MaximumRows;
}
