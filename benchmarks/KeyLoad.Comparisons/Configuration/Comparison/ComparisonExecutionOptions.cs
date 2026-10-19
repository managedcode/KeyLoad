using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;
/// <summary>Required operational limits shared by execution comparison adapters.</summary>
[ConfigurationOptions]
public sealed partial class ComparisonExecutionOptions
{
    internal static readonly TimeSpan MaximumHostLifetime = TimeSpan.FromHours(ComparisonOperationalPolicy.DefaultHostLifetimeHours);
    private const int MinimumPositiveLimit = 0;
    internal const string ComparisonOperationalLimitsMustBePresentPositive = "Execution comparison operational limits must be present, positive and consistent.";
    /// <summary>The required source-controlled configuration section.</summary>
    public const string SectionName = "ComparisonExecution";
    /// <summary>The execution operation deadline, equivalent to the frozen workload profile.</summary>
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
    public int PostgresMinimumPoolSize { get; set; } = ComparisonOperationalPolicy.DefaultPostgresMinimumPoolSize;
    /// <summary>The PostgreSQL prepared statement cache capacity.</summary>
    public int PostgresMaxAutoPrepare
    {
        get; set;
    }
    /// <summary>The usage threshold for execution PostgreSQL automatic preparation.</summary>
    public int PostgresAutoPrepareMinUsages
    {
        get; set;
    }
    /// <summary>The pooled execution HTTP read buffer capacity.</summary>
    public int ReadBufferBytes
    {
        get; set;
    }
    /// <summary>The maximum execution HTTP response body size in bytes.</summary>
    public int MaxResponseBytes
    {
        get; set;
    }
    /// <summary>The maximum documents submitted in one execution write batch.</summary>
    public int WriteBatchCapacity
    {
        get; set;
    }
    /// <summary>The maximum records requested in one ordered readback page.</summary>
    public int ReadbackBatchCapacity
    {
        get; set;
    }
    /// <summary>The joined execution cleanup deadline.</summary>
    public TimeSpan CleanupTimeout
    {
        get; set;
    }
    /// <summary>The execution asynchronous index operation deadline.</summary>
    public TimeSpan IndexBuildTimeout
    {
        get; set;
    }
    /// <summary>The interval between execution index completion checks.</summary>
    public TimeSpan IndexPollInterval
    {
        get; set;
    }
    /// <summary>The cadence of process resource observations, recorded with the actual samples.</summary>
    public int ResourceSampleIntervalMilliseconds { get; set; } = ComparisonOperationalPolicy.DefaultResourceSampleIntervalMilliseconds;
    /// <summary>The interval between progress lines while a workload phase is active.</summary>
    public TimeSpan ProgressHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(ComparisonOperationalPolicy.DefaultProgressHeartbeatSeconds);
    /// <summary>The joined comparison host lifetime, retaining the current maximum deadline.</summary>
    public TimeSpan HostLifetime { get; set; } = TimeSpan.FromHours(ComparisonOperationalPolicy.DefaultHostLifetimeHours);

    /// <summary>Validates the injected wrapper before any execution target is created.</summary>
    /// <param name="options">The centrally bound options.</param>
    /// <returns>The validated execution options wrapper.</returns>
    public static IOptions<ComparisonExecutionOptions> Require(IOptions<ComparisonExecutionOptions> options)
        => ComparisonOperationalPolicy.Require(options);

    /// <summary>Rejects omitted or inconsistent operational configuration.</summary>
    /// <returns>This validated policy.</returns>
    public ComparisonExecutionOptions Validate()
    {
        ComparisonOperationalPolicy.Validate(this);

        ComparisonAdapterOptionsValidator.Validate(this);
        ValidateMongoPolicy();
        ValidateVectorPolicy();
        DocumentPolicy.Validate(this);
        ComparisonSeedExecutionOptionsValidator.ValidateRequired(this);
        return this;
    }

    /// <summary>Retains effective operational settings alongside execution index evidence.</summary>
    /// <param name = "parameters">The actual execution index parameters.</param>
    public void RecordEvidence(IDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ComparisonOperationalPolicy.Record(this, parameters);
        RecordMongoEvidence(parameters);
        RecordVectorEvidence(parameters);
        RecordSeedEvidence(parameters);
    }
}
