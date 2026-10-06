using System.Globalization;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;
/// <summary>Required operational limits shared by native comparison adapters.</summary>
[ConfigurationOptions]
public sealed partial class NativeComparisonExecutionOptions
{
    private const int MinimumPositiveLimit = 0;
    internal const string NativeComparisonOperationalLimitsMustBePresentPositive = "Native comparison operational limits must be present, positive and consistent.";
    private const string DurationFormat = "c";
    private const int DefaultPostgresMinimumPoolSize = 10;
    private const int DefaultResourceSampleIntervalMilliseconds = 50;
    private const int DefaultProgressHeartbeatSeconds = 30;
    private const int DefaultHostLifetimeHours = 2;
    private const int MaximumResourceSampleIntervalMilliseconds = 1000;
    private static readonly TimeSpan MaximumHostLifetime = TimeSpan.FromHours(DefaultHostLifetimeHours);
    /// <summary>The required source-controlled configuration section.</summary>
    public const string SectionName = "NativeComparisonExecution";
    /// <summary>The native operation deadline, equivalent to the frozen workload profile.</summary>
    public TimeSpan OperationTimeout { get; set; }
    /// <summary>The PostgreSQL connection pool capacity.</summary>
    public int PostgresMaxPoolSize { get; set; }
    /// <summary>The retained minimum connection capacity for ordinary PostgreSQL workloads.</summary>
    public int PostgresMinimumPoolSize { get; set; } = DefaultPostgresMinimumPoolSize;
    /// <summary>The PostgreSQL prepared statement cache capacity.</summary>
    public int PostgresMaxAutoPrepare { get; set; }
    /// <summary>The usage threshold for native PostgreSQL automatic preparation.</summary>
    public int PostgresAutoPrepareMinUsages { get; set; }
    /// <summary>The pooled native HTTP read buffer capacity.</summary>
    public int ReadBufferBytes { get; set; }
    /// <summary>The maximum native HTTP response body size in bytes.</summary>
    public int MaxResponseBytes { get; set; }
    /// <summary>The maximum documents submitted in one native write batch.</summary>
    public int WriteBatchCapacity { get; set; }
    /// <summary>The maximum records requested in one ordered readback page.</summary>
    public int ReadbackBatchCapacity { get; set; }
    /// <summary>The joined native cleanup deadline.</summary>
    public TimeSpan CleanupTimeout { get; set; }
    /// <summary>The native asynchronous index operation deadline.</summary>
    public TimeSpan IndexBuildTimeout { get; set; }
    /// <summary>The interval between native index completion checks.</summary>
    public TimeSpan IndexPollInterval { get; set; }
    /// <summary>The cadence of process resource observations, recorded with the actual samples.</summary>
    public int ResourceSampleIntervalMilliseconds { get; set; } = DefaultResourceSampleIntervalMilliseconds;
    /// <summary>The interval between progress lines while a workload phase is active.</summary>
    public TimeSpan ProgressHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(DefaultProgressHeartbeatSeconds);
    /// <summary>The joined comparison host lifetime, retaining the current maximum deadline.</summary>
    public TimeSpan HostLifetime { get; set; } = TimeSpan.FromHours(DefaultHostLifetimeHours);

    /// <summary>Validates the injected wrapper before any native target is created.</summary>
    /// <param name="options">The centrally bound options.</param>
    /// <returns>The validated native options wrapper.</returns>
    public static IOptions<NativeComparisonExecutionOptions> Require(IOptions<NativeComparisonExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Value.Validate();
        return options;
    }

    /// <summary>Rejects omitted or inconsistent operational configuration.</summary>
    /// <returns>This validated policy.</returns>
    public NativeComparisonExecutionOptions Validate()
    {
        if (OperationTimeout <= TimeSpan.Zero || PostgresMaxPoolSize <= MinimumPositiveLimit || PostgresMinimumPoolSize <= MinimumPositiveLimit || PostgresMaxAutoPrepare <= MinimumPositiveLimit || PostgresAutoPrepareMinUsages <= MinimumPositiveLimit || MaxResponseBytes <= MinimumPositiveLimit || ReadBufferBytes <= MinimumPositiveLimit || ReadBufferBytes > MaxResponseBytes || WriteBatchCapacity <= MinimumPositiveLimit || ReadbackBatchCapacity <= MinimumPositiveLimit || WriteBatchCapacity > MaxResponseBytes || ReadbackBatchCapacity > MaxResponseBytes || CleanupTimeout <= TimeSpan.Zero || IndexBuildTimeout <= TimeSpan.Zero || IndexPollInterval <= TimeSpan.Zero || IndexPollInterval >= IndexBuildTimeout
            || ResourceSampleIntervalMilliseconds is <= MinimumPositiveLimit or > MaximumResourceSampleIntervalMilliseconds
            || ProgressHeartbeatInterval <= TimeSpan.Zero || ProgressHeartbeatInterval > HostLifetime
            || HostLifetime <= TimeSpan.Zero || HostLifetime > MaximumHostLifetime)
        {
            throw new OptionsValidationException(SectionName, typeof(NativeComparisonExecutionOptions), [NativeComparisonOperationalLimitsMustBePresentPositive]);
        }

        ValidateAdapterPolicy();
        ValidateMongoPolicy();
        ValidateReportPolicy();
        ValidateVectorPolicy();
        var seedValidation = NativeComparisonSeedExecutionOptionsValidator.Instance.Validate(Options.DefaultName, this);
        if (seedValidation.Failed)
        {
            throw new OptionsValidationException(SectionName, typeof(NativeComparisonExecutionOptions), seedValidation.Failures);
        }
        return this;
    }

    /// <summary>Retains effective operational settings alongside native index evidence.</summary>
    /// <param name = "parameters">The actual native index parameters.</param>
    public void RecordEvidence(IDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters[nameof(OperationTimeout)] = OperationTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
        parameters[nameof(PostgresMinimumPoolSize)] = PostgresMinimumPoolSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(PostgresMaxPoolSize)] = PostgresMaxPoolSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(PostgresMaxAutoPrepare)] = PostgresMaxAutoPrepare.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(PostgresAutoPrepareMinUsages)] = PostgresAutoPrepareMinUsages.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(ReadBufferBytes)] = ReadBufferBytes.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(MaxResponseBytes)] = MaxResponseBytes.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(WriteBatchCapacity)] = WriteBatchCapacity.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(ReadbackBatchCapacity)] = ReadbackBatchCapacity.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(CleanupTimeout)] = CleanupTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
        parameters[nameof(IndexBuildTimeout)] = IndexBuildTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
        parameters[nameof(IndexPollInterval)] = IndexPollInterval.ToString(DurationFormat, CultureInfo.InvariantCulture);
        RecordAdapterEvidence(parameters);
        RecordMongoEvidence(parameters);
        RecordVectorEvidence(parameters);
        RecordSeedEvidence(parameters);
    }
}
