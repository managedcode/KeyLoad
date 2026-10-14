using System.Globalization;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal static class NativeComparisonOperationalPolicy
{
    private const string DurationFormat = "c";
    private const int MinimumPositiveLimit = 0;
    internal const int DefaultPostgresMinimumPoolSize = 10;
    internal const int DefaultResourceSampleIntervalMilliseconds = 50;
    internal const int DefaultProgressHeartbeatSeconds = 30;
    internal const int DefaultHostLifetimeHours = 2;
    internal const int MaximumResourceSampleIntervalMilliseconds = 1000;

    internal static IOptions<NativeComparisonExecutionOptions> Require(IOptions<NativeComparisonExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Value.Validate();
        return options;
    }

    internal static void Validate(NativeComparisonExecutionOptions options)
    {
        if (options.OperationTimeout <= TimeSpan.Zero || options.PostgresMaxPoolSize <= MinimumPositiveLimit || options.PostgresMinimumPoolSize <= MinimumPositiveLimit || options.PostgresMaxAutoPrepare <= MinimumPositiveLimit || options.PostgresAutoPrepareMinUsages <= MinimumPositiveLimit || options.MaxResponseBytes <= MinimumPositiveLimit || options.ReadBufferBytes <= MinimumPositiveLimit || options.ReadBufferBytes > options.MaxResponseBytes || options.WriteBatchCapacity <= MinimumPositiveLimit || options.ReadbackBatchCapacity <= MinimumPositiveLimit || options.WriteBatchCapacity > options.MaxResponseBytes || options.ReadbackBatchCapacity > options.MaxResponseBytes || options.CleanupTimeout <= TimeSpan.Zero || options.IndexBuildTimeout <= TimeSpan.Zero || options.IndexPollInterval <= TimeSpan.Zero || options.IndexPollInterval >= options.IndexBuildTimeout
            || options.ResourceSampleIntervalMilliseconds is <= MinimumPositiveLimit or > MaximumResourceSampleIntervalMilliseconds
            || options.ProgressHeartbeatInterval <= TimeSpan.Zero || options.ProgressHeartbeatInterval > options.HostLifetime
            || options.HostLifetime <= TimeSpan.Zero || options.HostLifetime > NativeComparisonExecutionOptions.MaximumHostLifetime)
        {
            throw new OptionsValidationException(NativeComparisonExecutionOptions.SectionName, typeof(NativeComparisonExecutionOptions), [NativeComparisonExecutionOptions.NativeComparisonOperationalLimitsMustBePresentPositive]);
        }
    }

    internal static void Record(NativeComparisonExecutionOptions options, IDictionary<string, string> parameters)
    {
        parameters[nameof(options.OperationTimeout)] = options.OperationTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
        NativeDocumentPolicy.Record(options, parameters);
        parameters[nameof(options.PostgresMinimumPoolSize)] = options.PostgresMinimumPoolSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.PostgresMaxPoolSize)] = options.PostgresMaxPoolSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.PostgresMaxAutoPrepare)] = options.PostgresMaxAutoPrepare.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.PostgresAutoPrepareMinUsages)] = options.PostgresAutoPrepareMinUsages.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.ReadBufferBytes)] = options.ReadBufferBytes.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.MaxResponseBytes)] = options.MaxResponseBytes.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.WriteBatchCapacity)] = options.WriteBatchCapacity.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.ReadbackBatchCapacity)] = options.ReadbackBatchCapacity.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.CleanupTimeout)] = options.CleanupTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
        parameters[nameof(options.IndexBuildTimeout)] = options.IndexBuildTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
        parameters[nameof(options.IndexPollInterval)] = options.IndexPollInterval.ToString(DurationFormat, CultureInfo.InvariantCulture);
        NativeComparisonAdapterEvidence.Record(options, parameters);
    }
}
