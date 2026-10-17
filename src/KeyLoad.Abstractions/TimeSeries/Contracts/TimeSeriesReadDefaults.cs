namespace KeyLoad;

/// <summary>Defines caller defaults for bounded time-series statistics.</summary>
public static class TimeSeriesReadDefaults
{
    /// <summary>Default maximum raw samples examined before charged lookahead.</summary>
    public const int MaxSamples = 10_000;
    /// <summary>Default maximum dense windows returned by one operation.</summary>
    public const int MaxWindows = 1_000;
}
