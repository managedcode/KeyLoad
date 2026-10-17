using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Bounded policy for native comparison regression execution and joined cleanup.</summary>
[ConfigurationOptions]
internal sealed partial record NativeComparisonHarnessOptions
{
    internal const string SectionName = "KeyLoad:NativeComparisonHarness";
    internal const string ValidationMessage = "Native comparison regression policy is outside its supported bounds.";
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
            throw new OptionsValidationException(SectionName, typeof(NativeComparisonHarnessOptions), [ValidationMessage]);
        }
    }

    internal bool IsValid()
        => NativeComparisonHarnessOptionsValidator.IsCoreValid(this) && IsLogPolicyValid() && IsAuxiliaryValid();

    private static bool PositiveSeconds(TimeSpan value, int ceilingSeconds)
        => value > TimeSpan.Zero && value.Ticks <= ceilingSeconds * TimeSpan.TicksPerSecond;
}

internal sealed class NativeComparisonHarnessOptionsValidator : IValidateOptions<NativeComparisonHarnessOptions>
{
    public ValidateOptionsResult Validate(string? name, NativeComparisonHarnessOptions options)
        => options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(NativeComparisonHarnessOptions.ValidationMessage);

    internal static bool IsCoreValid(NativeComparisonHarnessOptions options)
        => PositiveSeconds(options.PostgresTargetDisposeTimeout, NativeComparisonHarnessOptions.TargetDisposeSeconds)
            && PositiveSeconds(options.PostgresSchemaCleanupTimeout, NativeComparisonHarnessOptions.SchemaCleanupSeconds)
            && PositiveSeconds(options.PostgresCancellationSettlementTimeout, NativeComparisonHarnessOptions.CancellationSettlementSeconds)
            && PositiveSeconds(options.PostgresRollbackTimeout, NativeComparisonHarnessOptions.RollbackSeconds)
            && PositiveSeconds(options.PostgresLockObservationTimeout, NativeComparisonHarnessOptions.LockObservationSeconds)
            && options.PostgresLockPollInterval > TimeSpan.Zero
            && options.PostgresLockPollInterval.Ticks <= NativeComparisonHarnessOptions.LockPollMilliseconds * TimeSpan.TicksPerMillisecond
            && options.PostgresLockPollInterval < options.PostgresLockObservationTimeout
            && PositiveSeconds(options.PostgresDatabaseCleanupTimeout, NativeComparisonHarnessOptions.DatabaseCleanupSeconds)
            && PositiveSeconds(options.PostgresContextResetTimeout, NativeComparisonHarnessOptions.ContextResetSeconds)
            && options.PostgresContextConnectTimeoutSeconds is >= NativeComparisonHarnessOptions.PositiveMinimum and <= NativeComparisonHarnessOptions.ContextConnectSeconds
            && options.PostgresContextConnectTimeoutSeconds * TimeSpan.TicksPerSecond <= options.PostgresContextResetTimeout.Ticks
            && options.RedisReadinessProbeExpiry > TimeSpan.Zero
            && options.RedisReadinessProbeExpiry.Ticks <= NativeComparisonHarnessOptions.ProbeExpiryMinutes * TimeSpan.TicksPerMinute
            && PositiveSeconds(options.RedisReadinessCancellationDelay, NativeComparisonHarnessOptions.ProbeCancellationSeconds)
            && options.RedisReadinessCancellationDelay < options.RedisReadinessProbeExpiry
            && PositiveSeconds(options.KurrentVolumeTimeout, NativeComparisonHarnessOptions.VolumeSeconds)
            && options.KurrentVolumeMaximumWorkers is >= NativeComparisonHarnessOptions.PositiveMinimum and <= NativeComparisonHarnessOptions.VolumeWorkers
            && options.KurrentGossipMaximumResponseBytes is >= NativeComparisonHarnessOptions.PositiveMinimum and <= NativeComparisonHarnessOptions.GossipResponseBytes
            && PositiveSeconds(options.OriginalTaskSettlementTimeout, NativeComparisonHarnessOptions.OriginalSettlementSeconds)
            && PositiveSeconds(options.TeardownReceiptTimeout, NativeComparisonHarnessOptions.ReceiptSeconds)
            && options.TeardownReceiptFileBufferBytes is >= NativeComparisonHarnessOptions.PositiveMinimum and <= NativeComparisonHarnessOptions.ReceiptFileBufferBytes;

    private static bool PositiveSeconds(TimeSpan value, int ceilingSeconds)
        => value > TimeSpan.Zero && value.Ticks <= ceilingSeconds * TimeSpan.TicksPerSecond;

}
