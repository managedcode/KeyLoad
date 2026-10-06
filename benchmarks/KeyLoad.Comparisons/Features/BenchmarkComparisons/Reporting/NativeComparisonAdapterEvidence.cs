using System.Globalization;

namespace KeyLoad.Comparisons;

internal static class NativeComparisonAdapterEvidence
{
    private const string DurationFormat = "c";

    internal static void Record(NativeComparisonExecutionOptions options, IDictionary<string, string> parameters)
    {
        parameters[nameof(options.KeyLoadGraphSeedBatchSize)] = options.KeyLoadGraphSeedBatchSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.Neo4jSeedBatchSize)] = options.Neo4jSeedBatchSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.Neo4jMaximumExecutionTimeSeconds)] = options.Neo4jMaximumExecutionTimeSeconds.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.TimeSeriesInitialReadCapacity)] = options.TimeSeriesInitialReadCapacity.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.KeyLoadTimeSeriesReadLimit)] = options.KeyLoadTimeSeriesReadLimit.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.ReportFileBufferBytes)] = options.ReportFileBufferBytes.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.OpenSearchHealthWaitTimeoutSeconds)] = options.OpenSearchHealthWaitTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.TimescaleConnectionTimeout)] = options.TimescaleConnectionTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
        parameters[nameof(options.TimescaleCommandTimeout)] = options.TimescaleCommandTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
        parameters[nameof(options.TimescaleCancellationTimeoutMilliseconds)] = options.TimescaleCancellationTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.TimescaleMinimumPoolSize)] = options.TimescaleMinimumPoolSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.TimescaleMaximumPoolSize)] = options.TimescaleMaximumPoolSize.ToString(CultureInfo.InvariantCulture);
    }
}
