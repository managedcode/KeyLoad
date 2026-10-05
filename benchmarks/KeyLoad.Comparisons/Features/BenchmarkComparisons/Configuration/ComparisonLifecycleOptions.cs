using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Bound readiness, lease polling and cleanup policy for native comparison targets.</summary>
[ConfigurationOptions]
public sealed class ComparisonLifecycleOptions
{
    /// <summary>The optional section for native lifecycle overrides.</summary>
    public const string SectionName = "ComparisonLifecycle";
    /// <summary>The rejection for invalid lifecycle timing.</summary>
    public const string ValidationMessage = "Comparison lifecycle deadlines and polling intervals must be positive, bounded and consistent.";
    private const int ReadinessSeconds = 60;
    private const int KeyLoadPollMilliseconds = 100;
    private const int PostgresPollMilliseconds = 200;
    private const int HttpPollMilliseconds = 250;
    private const int RedisPollMilliseconds = 200;
    private const int MongoPollMilliseconds = 100;
    private const int ClaimPollMilliseconds = 1;
    private const int QueueLeaseSeconds = 30;
    private const int DiagnosticSeconds = 2;
    private const int ReceiptSeconds = 3;
    private const int ProbeExpiryMinutes = 2;
    private const int Neo4jCleanupSeconds = 15;
    private const int MongoCleanupSeconds = 15;
    private const int QdrantCleanupSeconds = 10;
    private const int OpenSearchCleanupSeconds = 10;
    private const int TimescaleCleanupSeconds = 15;
    private const int KurrentCleanupSeconds = 120;
    private const int KurrentCleanupHostSeconds = 180;
    private const int KurrentPollMilliseconds = 100;
    private const int KurrentDeleteConcurrency = 16;
    private const int MaximumDeadlineMinutes = 10;

    /// <summary>The deadline for observing all required native replica copies.</summary>
    public TimeSpan ReadinessTimeout { get; set; } = TimeSpan.FromSeconds(ReadinessSeconds);
    /// <summary>The cadence for observing KeyLoad applied positions.</summary>
    public TimeSpan KeyLoadReadinessPollInterval { get; set; } = TimeSpan.FromMilliseconds(KeyLoadPollMilliseconds);
    /// <summary>The cadence for observing PostgreSQL replication state.</summary>
    public TimeSpan PostgresReadinessPollInterval { get; set; } = TimeSpan.FromMilliseconds(PostgresPollMilliseconds);
    /// <summary>The cadence for Qdrant and RabbitMQ native readiness requests.</summary>
    public TimeSpan HttpReadinessPollInterval { get; set; } = TimeSpan.FromMilliseconds(HttpPollMilliseconds);
    /// <summary>The cadence for direct Redis replica probes.</summary>
    public TimeSpan RedisReadinessPollInterval { get; set; } = TimeSpan.FromMilliseconds(RedisPollMilliseconds);
    /// <summary>The cadence for direct MongoDB replica probes.</summary>
    public TimeSpan MongoReadinessPollInterval { get; set; } = TimeSpan.FromMilliseconds(MongoPollMilliseconds);
    /// <summary>The cadence for retrying a queue claim without an admitted delivery.</summary>
    public TimeSpan QueueClaimPollInterval { get; set; } = TimeSpan.FromMilliseconds(ClaimPollMilliseconds);
    /// <summary>The equivalent lease duration used by the KeyLoad and PostgreSQL queue workloads.</summary>
    public TimeSpan QueueLeaseDuration { get; set; } = TimeSpan.FromSeconds(QueueLeaseSeconds);
    /// <summary>The deadline for private KeyLoad failure observation.</summary>
    public TimeSpan FailureObservationTimeout { get; set; } = TimeSpan.FromSeconds(DiagnosticSeconds);
    /// <summary>The native Redis fsync receipt deadline.</summary>
    public TimeSpan RedisReceiptTimeout { get; set; } = TimeSpan.FromSeconds(ReceiptSeconds);
    /// <summary>The expiry for the transient Redis replication probe key.</summary>
    public TimeSpan RedisProbeExpiry { get; set; } = TimeSpan.FromMinutes(ProbeExpiryMinutes);
    /// <summary>The deadline for removing the owned Neo4j corpus.</summary>
    public TimeSpan Neo4jCleanupTimeout { get; set; } = TimeSpan.FromSeconds(Neo4jCleanupSeconds);
    /// <summary>The deadline for removing the owned MongoDB corpus.</summary>
    public TimeSpan MongoCleanupTimeout { get; set; } = TimeSpan.FromSeconds(MongoCleanupSeconds);
    /// <summary>The deadline for removing the owned Qdrant corpus.</summary>
    public TimeSpan QdrantCleanupTimeout { get; set; } = TimeSpan.FromSeconds(QdrantCleanupSeconds);
    /// <summary>The deadline for removing the owned OpenSearch corpus.</summary>
    public TimeSpan OpenSearchCleanupTimeout { get; set; } = TimeSpan.FromSeconds(OpenSearchCleanupSeconds);
    /// <summary>The deadline for removing the owned Timescale corpus.</summary>
    public TimeSpan TimescaleCleanupTimeout { get; set; } = TimeSpan.FromSeconds(TimescaleCleanupSeconds);
    /// <summary>The deadline for deleting streams acknowledged to the current Kurrent run.</summary>
    public TimeSpan KurrentCleanupTimeout { get; set; } = TimeSpan.FromSeconds(KurrentCleanupSeconds);
    /// <summary>The total Kurrent cleanup and native-client disposal deadline.</summary>
    public TimeSpan KurrentCleanupHostTimeout { get; set; } = TimeSpan.FromSeconds(KurrentCleanupHostSeconds);
    /// <summary>The cadence for observing native Kurrent gossip and replica copies.</summary>
    public TimeSpan KurrentReadinessPollInterval { get; set; } = TimeSpan.FromMilliseconds(KurrentPollMilliseconds);
    /// <summary>The bounded number of concurrent owned-stream deletions.</summary>
    public int KurrentCleanupConcurrency { get; set; } = KurrentDeleteConcurrency;

    /// <summary>Checks native deadline bounds and prevents a poll interval from exceeding readiness.</summary>
    /// <returns>Whether every lifecycle value is usable.</returns>
    public bool IsValid() => Deadline(ReadinessTimeout) && Poll(KeyLoadReadinessPollInterval)
        && Poll(PostgresReadinessPollInterval) && Poll(HttpReadinessPollInterval)
        && Poll(RedisReadinessPollInterval) && Poll(MongoReadinessPollInterval) && Poll(QueueClaimPollInterval)
        && Deadline(QueueLeaseDuration)
        && Deadline(FailureObservationTimeout) && Deadline(RedisReceiptTimeout) && Deadline(RedisProbeExpiry)
        && Deadline(Neo4jCleanupTimeout) && Deadline(MongoCleanupTimeout) && Deadline(QdrantCleanupTimeout)
        && Deadline(OpenSearchCleanupTimeout) && Deadline(TimescaleCleanupTimeout)
        && Deadline(KurrentCleanupTimeout) && Deadline(KurrentCleanupHostTimeout)
        && KurrentCleanupHostTimeout >= KurrentCleanupTimeout && Poll(KurrentReadinessPollInterval)
        && KurrentCleanupConcurrency is > 0 and <= KurrentDeleteConcurrency;

    /// <summary>Rejects invalid standalone caller settings before target construction.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(ComparisonLifecycleOptions), [ValidationMessage]);
        }
    }

    private bool Poll(TimeSpan value) => Deadline(value) && value < ReadinessTimeout;
    private static bool Deadline(TimeSpan value) => value > TimeSpan.Zero
        && value <= TimeSpan.FromMinutes(MaximumDeadlineMinutes);
}
