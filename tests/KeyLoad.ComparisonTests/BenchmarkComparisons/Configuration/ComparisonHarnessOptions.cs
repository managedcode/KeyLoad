using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Bounded policy for execution comparison checks execution and joined cleanup.</summary>
[ConfigurationOptions]
internal sealed partial record ComparisonHarnessOptions
{
    internal const string SectionName = "KeyLoad:ComparisonHarness";
    internal const string ValidationMessage = "Execution comparison checks policy is outside its supported bounds.";
    internal const int PositiveMinimum = 1;
    internal const int TargetDisposeSeconds = 15, SchemaCleanupSeconds = 10, CancellationSettlementSeconds = 10;
    internal const int RollbackSeconds = 10, LockObservationSeconds = 20, LockPollMilliseconds = 50;
    internal const int DatabaseCleanupSeconds = 15, ContextResetSeconds = 30, ContextConnectSeconds = 5;
    internal const int ProbeExpiryMinutes = 2, ProbeCancellationSeconds = 1;
    internal const int VolumeSeconds = 900, VolumeWorkers = 16, GossipResponseBytes = 65_536;
    internal const int OriginalSettlementSeconds = 30, ReceiptSeconds = 30, ReceiptFileBufferBytes = 4096;

    public TimeSpan PostgresTargetDisposeTimeout { get; init; } = TimeSpan.FromSeconds(TargetDisposeSeconds);
    public TimeSpan PostgresSchemaCleanupTimeout { get; init; } = TimeSpan.FromSeconds(SchemaCleanupSeconds);
    public TimeSpan PostgresCancellationSettlementTimeout { get; init; } = TimeSpan.FromSeconds(CancellationSettlementSeconds);
    public TimeSpan PostgresRollbackTimeout { get; init; } = TimeSpan.FromSeconds(RollbackSeconds);
    public TimeSpan PostgresLockObservationTimeout { get; init; } = TimeSpan.FromSeconds(LockObservationSeconds);
    public TimeSpan PostgresLockPollInterval { get; init; } = TimeSpan.FromMilliseconds(LockPollMilliseconds);
    public TimeSpan PostgresDatabaseCleanupTimeout { get; init; } = TimeSpan.FromSeconds(DatabaseCleanupSeconds);
    public TimeSpan PostgresContextResetTimeout { get; init; } = TimeSpan.FromSeconds(ContextResetSeconds);
    public int PostgresContextConnectTimeoutSeconds { get; init; } = ContextConnectSeconds;
    public TimeSpan RedisReadinessProbeExpiry { get; init; } = TimeSpan.FromMinutes(ProbeExpiryMinutes);
    public TimeSpan RedisReadinessCancellationDelay { get; init; } = TimeSpan.FromSeconds(ProbeCancellationSeconds);
    public TimeSpan KurrentVolumeTimeout { get; init; } = TimeSpan.FromSeconds(VolumeSeconds);
    public int KurrentVolumeMaximumWorkers { get; init; } = VolumeWorkers;
    public int KurrentGossipMaximumResponseBytes { get; init; } = GossipResponseBytes;
    public TimeSpan OriginalTaskSettlementTimeout { get; init; } = TimeSpan.FromSeconds(OriginalSettlementSeconds);
    public TimeSpan TeardownReceiptTimeout { get; init; } = TimeSpan.FromSeconds(ReceiptSeconds);
    public int TeardownReceiptFileBufferBytes { get; init; } = ReceiptFileBufferBytes;

    internal void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(ComparisonHarnessOptions), [ValidationMessage]);
        }
    }

    internal bool IsValid()
        => ComparisonHarnessOptionsValidator.IsCoreValid(this) && IsLogPolicyValid() && IsAuxiliaryValid();

    private static bool PositiveSeconds(TimeSpan value, int ceilingSeconds)
        => value > TimeSpan.Zero && value.Ticks <= ceilingSeconds * TimeSpan.TicksPerSecond;
}
