namespace KeyLoad.Comparisons;

/// <summary>Adapter policies consumed from the same validated native execution group.</summary>
public sealed partial class NativeComparisonExecutionOptions
{
    internal const int DefaultTimescaleConnectionTimeoutSeconds = 30;
    internal const int DefaultTimescaleCommandTimeoutSeconds = 30;
    internal const int DefaultKeyLoadGraphSeedBatchSize = 100;
    internal const int DefaultNeo4jSeedBatchSize = 256;
    internal const int DefaultNeo4jMaximumExecutionTimeSeconds = 30;
    internal const int DefaultTimeSeriesInitialReadCapacity = 16;
    internal const int DefaultKeyLoadTimeSeriesReadLimit = 1000;
    internal const int DefaultReportFileBufferBytes = 65_536;
    internal const int DefaultOpenSearchHealthWaitTimeoutSeconds = 60;
    internal const int DefaultTimescaleCancellationTimeoutMilliseconds = 2000;
    internal const int DefaultTimescaleMaximumPoolSize = 16;
    /// <summary>The maximum graph vertices seeded in one KeyLoad request.</summary>
    public int KeyLoadGraphSeedBatchSize { get; set; } = DefaultKeyLoadGraphSeedBatchSize;
    /// <summary>The maximum native documents or edges in one Neo4j seed transaction.</summary>
    public int Neo4jSeedBatchSize { get; set; } = DefaultNeo4jSeedBatchSize;
    /// <summary>The native Neo4j query execution limit in whole seconds.</summary>
    public int Neo4jMaximumExecutionTimeSeconds { get; set; } = DefaultNeo4jMaximumExecutionTimeSeconds;
    /// <summary>The initial capacity of retained time-series read results.</summary>
    public int TimeSeriesInitialReadCapacity { get; set; } = DefaultTimeSeriesInitialReadCapacity;
    /// <summary>The maximum samples requested by the ordinary KeyLoad TimeSeries adapter.</summary>
    public int KeyLoadTimeSeriesReadLimit { get; set; } = DefaultKeyLoadTimeSeriesReadLimit;
    /// <summary>The native asynchronous report file buffer capacity.</summary>
    public int ReportFileBufferBytes { get; set; } = DefaultReportFileBufferBytes;
    /// <summary>The native OpenSearch cluster-health observation deadline in whole seconds.</summary>
    public int OpenSearchHealthWaitTimeoutSeconds { get; set; } = DefaultOpenSearchHealthWaitTimeoutSeconds;
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
}
