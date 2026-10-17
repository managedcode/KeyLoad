using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

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
