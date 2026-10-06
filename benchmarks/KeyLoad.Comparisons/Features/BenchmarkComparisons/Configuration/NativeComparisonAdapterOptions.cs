using System.Globalization;

namespace KeyLoad.Comparisons;

/// <summary>Adapter policies consumed from the same validated native execution group.</summary>
public sealed partial class NativeComparisonExecutionOptions
{
    private const int DefaultTimescaleConnectionTimeoutSeconds = 30;
    private const int DefaultTimescaleCommandTimeoutSeconds = 30;
    private const int DefaultKeyLoadGraphSeedBatchSize = 100;
    private const int DefaultNeo4jSeedBatchSize = 256;
    private const int DefaultNeo4jMaximumExecutionTimeSeconds = 30;
    private const int DefaultTimeSeriesInitialReadCapacity = 16;
    private const int DefaultReportFileBufferBytes = 65_536;
    private const int DefaultTimescaleCancellationTimeoutMilliseconds = 2000;
    private const int DefaultTimescaleMaximumPoolSize = 16;
    /// <summary>The maximum graph vertices seeded in one KeyLoad request.</summary>
    public int KeyLoadGraphSeedBatchSize { get; set; } = DefaultKeyLoadGraphSeedBatchSize;
    /// <summary>The maximum native documents or edges in one Neo4j seed transaction.</summary>
    public int Neo4jSeedBatchSize { get; set; } = DefaultNeo4jSeedBatchSize;
    /// <summary>The native Neo4j query execution limit in whole seconds.</summary>
    public int Neo4jMaximumExecutionTimeSeconds { get; set; } = DefaultNeo4jMaximumExecutionTimeSeconds;
    /// <summary>The initial capacity of retained time-series read results.</summary>
    public int TimeSeriesInitialReadCapacity { get; set; } = DefaultTimeSeriesInitialReadCapacity;
    /// <summary>The native asynchronous report file buffer capacity.</summary>
    public int ReportFileBufferBytes { get; set; } = DefaultReportFileBufferBytes;
    /// <summary>The native Timescale connection establishment deadline.</summary>
    public TimeSpan TimescaleConnectionTimeout { get; set; } = TimeSpan.FromSeconds(DefaultTimescaleConnectionTimeoutSeconds);
    /// <summary>The native Timescale command deadline.</summary>
    public TimeSpan TimescaleCommandTimeout { get; set; } = TimeSpan.FromSeconds(DefaultTimescaleCommandTimeoutSeconds);
    /// <summary>The native Timescale cancellation acknowledgement deadline.</summary>
    public int TimescaleCancellationTimeoutMilliseconds { get; set; } = DefaultTimescaleCancellationTimeoutMilliseconds;
    /// <summary>The minimum retained native Timescale connection pool capacity.</summary>
    public int TimescaleMinimumPoolSize { get; set; } = MinimumPositiveLimit;
    /// <summary>The maximum native Timescale connection pool capacity.</summary>
    public int TimescaleMaximumPoolSize { get; set; } = DefaultTimescaleMaximumPoolSize;

    private void ValidateAdapterPolicy()
    {
        if (KeyLoadGraphSeedBatchSize is <= MinimumPositiveLimit or > DefaultKeyLoadGraphSeedBatchSize
            || Neo4jSeedBatchSize is <= MinimumPositiveLimit or > DefaultNeo4jSeedBatchSize
            || Neo4jMaximumExecutionTimeSeconds is <= MinimumPositiveLimit or > DefaultNeo4jMaximumExecutionTimeSeconds
            || TimeSeriesInitialReadCapacity is <= MinimumPositiveLimit or > DefaultTimeSeriesInitialReadCapacity
            || ReportFileBufferBytes is <= MinimumPositiveLimit or > DefaultReportFileBufferBytes
            || !IsNativeSecondsTimeout(TimescaleConnectionTimeout, DefaultTimescaleConnectionTimeoutSeconds)
            || !IsNativeSecondsTimeout(TimescaleCommandTimeout, DefaultTimescaleCommandTimeoutSeconds)
            || TimescaleCancellationTimeoutMilliseconds is <= MinimumPositiveLimit or > DefaultTimescaleCancellationTimeoutMilliseconds || TimescaleMinimumPoolSize < MinimumPositiveLimit
            || TimescaleMaximumPoolSize <= MinimumPositiveLimit || TimescaleMaximumPoolSize > DefaultTimescaleMaximumPoolSize
            || TimescaleMinimumPoolSize > TimescaleMaximumPoolSize)
        {
            throw new Microsoft.Extensions.Options.OptionsValidationException(SectionName, typeof(NativeComparisonExecutionOptions),
                [NativeComparisonOperationalLimitsMustBePresentPositive]);
        }
    }

    private static bool IsNativeSecondsTimeout(TimeSpan timeout, int maximumSeconds)
        => timeout > TimeSpan.Zero && timeout.TotalSeconds <= maximumSeconds
            && timeout.Ticks % TimeSpan.TicksPerSecond == MinimumPositiveLimit;

    private void RecordAdapterEvidence(IDictionary<string, string> parameters)
    {
        parameters[nameof(KeyLoadGraphSeedBatchSize)] = KeyLoadGraphSeedBatchSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(Neo4jSeedBatchSize)] = Neo4jSeedBatchSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(Neo4jMaximumExecutionTimeSeconds)] = Neo4jMaximumExecutionTimeSeconds.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(TimeSeriesInitialReadCapacity)] = TimeSeriesInitialReadCapacity.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(ReportFileBufferBytes)] = ReportFileBufferBytes.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(TimescaleConnectionTimeout)] = TimescaleConnectionTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
        parameters[nameof(TimescaleCommandTimeout)] = TimescaleCommandTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
        parameters[nameof(TimescaleCancellationTimeoutMilliseconds)] = TimescaleCancellationTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(TimescaleMinimumPoolSize)] = TimescaleMinimumPoolSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(TimescaleMaximumPoolSize)] = TimescaleMaximumPoolSize.ToString(CultureInfo.InvariantCulture);
    }
}
