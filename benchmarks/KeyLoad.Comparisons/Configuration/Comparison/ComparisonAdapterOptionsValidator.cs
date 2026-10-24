using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal static class ComparisonAdapterOptionsValidator
{
    private const int Zero = 0;

    internal static void Validate(ComparisonExecutionOptions options)
    {
        if (options.ClickHouseInsertFlushMilliseconds is <= Zero or > ComparisonExecutionOptions.DefaultClickHouseInsertFlushMilliseconds
            || options.KeyLoadGraphSeedBatchSize is <= Zero or > ComparisonExecutionOptions.DefaultKeyLoadGraphSeedBatchSize
            || options.Neo4jSeedBatchSize is <= Zero or > ComparisonExecutionOptions.DefaultNeo4jSeedBatchSize
            || options.Neo4jMaximumExecutionTimeSeconds is <= Zero or > ComparisonExecutionOptions.DefaultNeo4jMaximumExecutionTimeSeconds
            || options.TimeSeriesInitialReadCapacity is <= Zero or > ComparisonExecutionOptions.DefaultTimeSeriesInitialReadCapacity
            || options.KeyLoadTimeSeriesReadLimit is <= Zero or > ComparisonExecutionOptions.DefaultKeyLoadTimeSeriesReadLimit
            || options.ReportFileBufferBytes is <= Zero or > ComparisonExecutionOptions.DefaultReportFileBufferBytes
            || options.OpenSearchHealthWaitTimeoutSeconds is <= Zero or > ComparisonExecutionOptions.DefaultOpenSearchHealthWaitTimeoutSeconds
            || !IsSecondsTimeout(options.TimescaleConnectionTimeout, ComparisonExecutionOptions.DefaultTimescaleConnectionTimeoutSeconds)
            || !IsSecondsTimeout(options.TimescaleCommandTimeout, ComparisonExecutionOptions.DefaultTimescaleCommandTimeoutSeconds)
            || options.TimescaleCancellationTimeoutMilliseconds is <= Zero or > ComparisonExecutionOptions.DefaultTimescaleCancellationTimeoutMilliseconds
            || options.TimescaleMinimumPoolSize < Zero || options.TimescaleMaximumPoolSize <= Zero
            || options.TimescaleMaximumPoolSize > ComparisonExecutionOptions.DefaultTimescaleMaximumPoolSize
            || options.TimescaleMinimumPoolSize > options.TimescaleMaximumPoolSize)
        {
            throw new OptionsValidationException(ComparisonExecutionOptions.SectionName,
                typeof(ComparisonExecutionOptions),
                [ComparisonExecutionOptions.ComparisonOperationalLimitsMustBePresentPositive]);
        }
    }

    private static bool IsSecondsTimeout(TimeSpan timeout, int maximumSeconds)
        => timeout > TimeSpan.Zero && timeout.TotalSeconds <= maximumSeconds
            && timeout.Ticks % TimeSpan.TicksPerSecond == Zero;
}
