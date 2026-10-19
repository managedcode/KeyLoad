using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonHarnessOptionsValidator : IValidateOptions<ComparisonHarnessOptions>
{
    public ValidateOptionsResult Validate(string? name, ComparisonHarnessOptions options)
        => options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(ComparisonHarnessOptions.ValidationMessage);

    internal static bool IsCoreValid(ComparisonHarnessOptions options)
        => PositiveSeconds(options.PostgresTargetDisposeTimeout, ComparisonHarnessOptions.TargetDisposeSeconds)
           && PositiveSeconds(options.PostgresSchemaCleanupTimeout, ComparisonHarnessOptions.SchemaCleanupSeconds)
           && PositiveSeconds(options.PostgresCancellationSettlementTimeout, ComparisonHarnessOptions.CancellationSettlementSeconds)
           && PositiveSeconds(options.PostgresRollbackTimeout, ComparisonHarnessOptions.RollbackSeconds)
           && PositiveSeconds(options.PostgresLockObservationTimeout, ComparisonHarnessOptions.LockObservationSeconds)
           && options.PostgresLockPollInterval > TimeSpan.Zero
           && options.PostgresLockPollInterval.Ticks <= ComparisonHarnessOptions.LockPollMilliseconds * TimeSpan.TicksPerMillisecond
           && options.PostgresLockPollInterval < options.PostgresLockObservationTimeout
           && PositiveSeconds(options.PostgresDatabaseCleanupTimeout, ComparisonHarnessOptions.DatabaseCleanupSeconds)
           && PositiveSeconds(options.PostgresContextResetTimeout, ComparisonHarnessOptions.ContextResetSeconds)
           && options.PostgresContextConnectTimeoutSeconds is >= ComparisonHarnessOptions.PositiveMinimum and <= ComparisonHarnessOptions.ContextConnectSeconds
           && options.PostgresContextConnectTimeoutSeconds * TimeSpan.TicksPerSecond <= options.PostgresContextResetTimeout.Ticks
           && options.RedisReadinessProbeExpiry > TimeSpan.Zero
           && options.RedisReadinessProbeExpiry.Ticks <= ComparisonHarnessOptions.ProbeExpiryMinutes * TimeSpan.TicksPerMinute
           && PositiveSeconds(options.RedisReadinessCancellationDelay, ComparisonHarnessOptions.ProbeCancellationSeconds)
           && options.RedisReadinessCancellationDelay < options.RedisReadinessProbeExpiry
           && PositiveSeconds(options.KurrentVolumeTimeout, ComparisonHarnessOptions.VolumeSeconds)
           && options.KurrentVolumeMaximumWorkers is >= ComparisonHarnessOptions.PositiveMinimum and <= ComparisonHarnessOptions.VolumeWorkers
           && options.KurrentGossipMaximumResponseBytes is >= ComparisonHarnessOptions.PositiveMinimum and <= ComparisonHarnessOptions.GossipResponseBytes
           && PositiveSeconds(options.OriginalTaskSettlementTimeout, ComparisonHarnessOptions.OriginalSettlementSeconds)
           && PositiveSeconds(options.TeardownReceiptTimeout, ComparisonHarnessOptions.ReceiptSeconds)
           && options.TeardownReceiptFileBufferBytes is >= ComparisonHarnessOptions.PositiveMinimum and <= ComparisonHarnessOptions.ReceiptFileBufferBytes;

    private static bool PositiveSeconds(TimeSpan value, int ceilingSeconds)
        => value > TimeSpan.Zero && value.Ticks <= ceilingSeconds * TimeSpan.TicksPerSecond;

}
