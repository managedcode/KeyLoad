using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeComparisonDiagnosticPolicyTests
{
    [Test]
    public async Task NativeFactoryAdmitsOriginalInclusiveCeilings()
    {
        var options = Create(static _ => { }).Value;
        await Assert.That(options.KurrentSetupMaximumCauses).IsEqualTo(3);
        await Assert.That(options.KurrentSetupMaximumFramesPerCause).IsEqualTo(8);
        await Assert.That(options.KurrentSetupMaximumIdentifierCharacters).IsEqualTo(64);
        await Assert.That(options.KurrentSetupBuilderCapacity).IsEqualTo(4096);
        await Assert.That(options.KurrentCleanupMaximumExceptionDepth).IsEqualTo(8);
        await Assert.That(options.KurrentCleanupMaximumCharacters).IsEqualTo(4096);
        await Assert.That(options.RedisReplicaMaximumCharacters).IsEqualTo(4096);
        await Assert.That(options.KeyLoadOutboxMaximumBytes).IsEqualTo(512);
        await Assert.That(options.KeyLoadOutboxMaximumConsumers).IsEqualTo(64);
    }

    [Test]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentSetupMaximumCauses), 0)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentSetupMaximumCauses), 4)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentSetupMaximumFramesPerCause), 0)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentSetupMaximumFramesPerCause), 9)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentSetupMaximumIdentifierCharacters), 0)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentSetupMaximumIdentifierCharacters), 65)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentSetupBuilderCapacity), 0)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentSetupBuilderCapacity), 4097)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentCleanupMaximumExceptionDepth), 0)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentCleanupMaximumExceptionDepth), 9)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentCleanupMaximumCharacters), 0)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KurrentCleanupMaximumCharacters), 4097)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.RedisReplicaMaximumCharacters), 0)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.RedisReplicaMaximumCharacters), 4097)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KeyLoadOutboxMaximumBytes), 0)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KeyLoadOutboxMaximumBytes), 513)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KeyLoadOutboxMaximumConsumers), 0)]
    [Arguments(nameof(NativeComparisonDiagnosticOptions.KeyLoadOutboxMaximumConsumers), 65)]
    public async Task NativeFactoryRejectsInvalidPolicyBeforeAnyFormatter(string field, int value)
    {
        var options = Create(settings => Set(settings, field, value));
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => Task.FromResult(options.Value));
    }

    [Test]
    public async Task NativeFactoryRejectsCapsThatCannotHoldTheirImmutableFallback()
    {
        const int BelowMinimum = 1;
        var redis = Create(settings => settings.RedisReplicaMaximumCharacters = RedisReplicaDiagnostics.Overflow.Length - BelowMinimum);
        var outbox = Create(settings => settings.KeyLoadOutboxMaximumBytes = KeyLoadOutboxDiagnosticLine.UnavailableLine.Length - BelowMinimum);
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => Task.FromResult(redis.Value));
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => Task.FromResult(outbox.Value));
    }

    private static OptionsManager<NativeComparisonDiagnosticOptions> Create(Action<NativeComparisonDiagnosticOptions> configure)
        => new(new OptionsFactory<NativeComparisonDiagnosticOptions>(
            [new ConfigureNamedOptions<NativeComparisonDiagnosticOptions>(Options.DefaultName, configure)], [],
            [new NativeComparisonDiagnosticOptionsValidator()]));

    private static void Set(NativeComparisonDiagnosticOptions options, string field, int value)
    {
        switch (field)
        {
            case nameof(options.KurrentSetupMaximumCauses):
                options.KurrentSetupMaximumCauses = value;
                break;
            case nameof(options.KurrentSetupMaximumFramesPerCause):
                options.KurrentSetupMaximumFramesPerCause = value;
                break;
            case nameof(options.KurrentSetupMaximumIdentifierCharacters):
                options.KurrentSetupMaximumIdentifierCharacters = value;
                break;
            case nameof(options.KurrentSetupBuilderCapacity):
                options.KurrentSetupBuilderCapacity = value;
                break;
            case nameof(options.KurrentCleanupMaximumExceptionDepth):
                options.KurrentCleanupMaximumExceptionDepth = value;
                break;
            case nameof(options.KurrentCleanupMaximumCharacters):
                options.KurrentCleanupMaximumCharacters = value;
                break;
            case nameof(options.RedisReplicaMaximumCharacters):
                options.RedisReplicaMaximumCharacters = value;
                break;
            case nameof(options.KeyLoadOutboxMaximumBytes):
                options.KeyLoadOutboxMaximumBytes = value;
                break;
            case nameof(options.KeyLoadOutboxMaximumConsumers):
                options.KeyLoadOutboxMaximumConsumers = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }
    }
}
