using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal static class NativeComparisonAdapterOptionsValidator
{
    private const int Zero = 0;

    internal static void Validate(NativeComparisonExecutionOptions options)
    {
        if (options.KeyLoadGraphSeedBatchSize is <= Zero or > NativeComparisonExecutionOptions.DefaultKeyLoadGraphSeedBatchSize
            || options.Neo4jSeedBatchSize is <= Zero or > NativeComparisonExecutionOptions.DefaultNeo4jSeedBatchSize
            || options.Neo4jMaximumExecutionTimeSeconds is <= Zero or > NativeComparisonExecutionOptions.DefaultNeo4jMaximumExecutionTimeSeconds
            || options.TimeSeriesInitialReadCapacity is <= Zero or > NativeComparisonExecutionOptions.DefaultTimeSeriesInitialReadCapacity
            || options.KeyLoadTimeSeriesReadLimit is <= Zero or > NativeComparisonExecutionOptions.DefaultKeyLoadTimeSeriesReadLimit
            || options.ReportFileBufferBytes is <= Zero or > NativeComparisonExecutionOptions.DefaultReportFileBufferBytes
            || options.OpenSearchHealthWaitTimeoutSeconds is <= Zero or > NativeComparisonExecutionOptions.DefaultOpenSearchHealthWaitTimeoutSeconds
            || !IsNativeSecondsTimeout(options.TimescaleConnectionTimeout, NativeComparisonExecutionOptions.DefaultTimescaleConnectionTimeoutSeconds)
            || !IsNativeSecondsTimeout(options.TimescaleCommandTimeout, NativeComparisonExecutionOptions.DefaultTimescaleCommandTimeoutSeconds)
            || options.TimescaleCancellationTimeoutMilliseconds is <= Zero or > NativeComparisonExecutionOptions.DefaultTimescaleCancellationTimeoutMilliseconds
            || options.TimescaleMinimumPoolSize < Zero || options.TimescaleMaximumPoolSize <= Zero
            || options.TimescaleMaximumPoolSize > NativeComparisonExecutionOptions.DefaultTimescaleMaximumPoolSize
            || options.TimescaleMinimumPoolSize > options.TimescaleMaximumPoolSize)
        {
            throw new OptionsValidationException(NativeComparisonExecutionOptions.SectionName,
                typeof(NativeComparisonExecutionOptions),
                [NativeComparisonExecutionOptions.NativeComparisonOperationalLimitsMustBePresentPositive]);
        }
    }

    private static bool IsNativeSecondsTimeout(TimeSpan timeout, int maximumSeconds)
        => timeout > TimeSpan.Zero && timeout.TotalSeconds <= maximumSeconds
            && timeout.Ticks % TimeSpan.TicksPerSecond == Zero;
}
