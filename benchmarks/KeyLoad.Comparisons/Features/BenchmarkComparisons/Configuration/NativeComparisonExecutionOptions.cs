using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;
/// <summary>Required operational limits shared by native comparison adapters.</summary>
[ConfigurationOptions]
public sealed partial class NativeComparisonExecutionOptions
{
    internal static readonly TimeSpan MaximumHostLifetime = TimeSpan.FromHours(NativeComparisonOperationalPolicy.DefaultHostLifetimeHours);
    private const int MinimumPositiveLimit = 0;
    internal const string NativeComparisonOperationalLimitsMustBePresentPositive = "Native comparison operational limits must be present, positive and consistent.";
    /// <summary>The required source-controlled configuration section.</summary>
    public const string SectionName = "NativeComparisonExecution";
    /// <summary>The native operation deadline, equivalent to the frozen workload profile.</summary>
    public TimeSpan OperationTimeout
    {
        get; set;
    }
    /// <summary>The PostgreSQL connection pool capacity.</summary>
    public int PostgresMaxPoolSize
    {
        get; set;
    }
    /// <summary>The retained minimum connection capacity for ordinary PostgreSQL workloads.</summary>
    public int PostgresMinimumPoolSize { get; set; } = NativeComparisonOperationalPolicy.DefaultPostgresMinimumPoolSize;
    /// <summary>The PostgreSQL prepared statement cache capacity.</summary>
    public int PostgresMaxAutoPrepare
    {
        get; set;
    }
    /// <summary>The usage threshold for native PostgreSQL automatic preparation.</summary>
    public int PostgresAutoPrepareMinUsages
    {
        get; set;
    }
    /// <summary>The pooled native HTTP read buffer capacity.</summary>
    public int ReadBufferBytes
    {
        get; set;
    }
    /// <summary>The maximum native HTTP response body size in bytes.</summary>
    public int MaxResponseBytes
    {
        get; set;
    }
    /// <summary>The maximum documents submitted in one native write batch.</summary>
    public int WriteBatchCapacity
    {
        get; set;
    }
    /// <summary>The maximum records requested in one ordered readback page.</summary>
    public int ReadbackBatchCapacity
    {
        get; set;
    }
    /// <summary>The joined native cleanup deadline.</summary>
    public TimeSpan CleanupTimeout
    {
        get; set;
    }
    /// <summary>The native asynchronous index operation deadline.</summary>
    public TimeSpan IndexBuildTimeout
    {
        get; set;
    }
    /// <summary>The interval between native index completion checks.</summary>
    public TimeSpan IndexPollInterval
    {
        get; set;
    }
    /// <summary>The cadence of process resource observations, recorded with the actual samples.</summary>
    public int ResourceSampleIntervalMilliseconds { get; set; } = NativeComparisonOperationalPolicy.DefaultResourceSampleIntervalMilliseconds;
    /// <summary>The interval between progress lines while a workload phase is active.</summary>
    public TimeSpan ProgressHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(NativeComparisonOperationalPolicy.DefaultProgressHeartbeatSeconds);
    /// <summary>The joined comparison host lifetime, retaining the current maximum deadline.</summary>
    public TimeSpan HostLifetime { get; set; } = TimeSpan.FromHours(NativeComparisonOperationalPolicy.DefaultHostLifetimeHours);

    /// <summary>Validates the injected wrapper before any native target is created.</summary>
    /// <param name="options">The centrally bound options.</param>
    /// <returns>The validated native options wrapper.</returns>
    public static IOptions<NativeComparisonExecutionOptions> Require(IOptions<NativeComparisonExecutionOptions> options)
        => NativeComparisonOperationalPolicy.Require(options);

    /// <summary>Rejects omitted or inconsistent operational configuration.</summary>
    /// <returns>This validated policy.</returns>
    public NativeComparisonExecutionOptions Validate()
    {
        NativeComparisonOperationalPolicy.Validate(this);

        NativeComparisonAdapterOptionsValidator.Validate(this);
        ValidateMongoPolicy();
        ValidateReportPolicy();
        ValidateVectorPolicy();
        NativeDocumentPolicy.Validate(this);
        NativeComparisonSeedExecutionOptionsValidator.ValidateRequired(this);
        return this;
    }

    /// <summary>Retains effective operational settings alongside native index evidence.</summary>
    /// <param name = "parameters">The actual native index parameters.</param>
    public void RecordEvidence(IDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        NativeComparisonOperationalPolicy.Record(this, parameters);
        RecordMongoEvidence(parameters);
        RecordVectorEvidence(parameters);
        RecordSeedEvidence(parameters);
    }
}
