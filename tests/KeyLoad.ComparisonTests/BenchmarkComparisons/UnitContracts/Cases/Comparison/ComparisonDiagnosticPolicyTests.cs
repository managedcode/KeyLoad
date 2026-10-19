using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonDiagnosticPolicyTests
{
    [Test]
    public async Task FactoryAdmitsOriginalInclusiveCeilings()
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
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentSetupMaximumCauses), 0)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentSetupMaximumCauses), 4)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentSetupMaximumFramesPerCause), 0)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentSetupMaximumFramesPerCause), 9)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentSetupMaximumIdentifierCharacters), 0)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentSetupMaximumIdentifierCharacters), 65)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentSetupBuilderCapacity), 0)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentSetupBuilderCapacity), 4097)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentCleanupMaximumExceptionDepth), 0)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentCleanupMaximumExceptionDepth), 9)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentCleanupMaximumCharacters), 0)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KurrentCleanupMaximumCharacters), 4097)]
    [Arguments(nameof(ComparisonDiagnosticOptions.RedisReplicaMaximumCharacters), 0)]
    [Arguments(nameof(ComparisonDiagnosticOptions.RedisReplicaMaximumCharacters), 4097)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KeyLoadOutboxMaximumBytes), 0)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KeyLoadOutboxMaximumBytes), 513)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KeyLoadOutboxMaximumConsumers), 0)]
    [Arguments(nameof(ComparisonDiagnosticOptions.KeyLoadOutboxMaximumConsumers), 65)]
    public async Task FactoryRejectsInvalidPolicyBeforeAnyFormatter(string field, int value)
    {
        var options = Create(settings => Set(settings, field, value));
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => Task.FromResult(options.Value));
    }

    [Test]
    public async Task FactoryRejectsCapsThatCannotHoldTheirImmutableFailureProjection()
    {
        const int BelowMinimum = 1;
        var redis = Create(settings => settings.RedisReplicaMaximumCharacters = RedisReplicaDiagnostics.Overflow.Length - BelowMinimum);
        var outbox = Create(settings => settings.KeyLoadOutboxMaximumBytes = KeyLoadOutboxDiagnosticLine.UnavailableLine.Length - BelowMinimum);
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => Task.FromResult(redis.Value));
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => Task.FromResult(outbox.Value));
    }

    private static OptionsManager<ComparisonDiagnosticOptions> Create(Action<ComparisonDiagnosticOptions> configure)
        => new(new OptionsFactory<ComparisonDiagnosticOptions>(
            [new ConfigureNamedOptions<ComparisonDiagnosticOptions>(Options.DefaultName, configure)], [],
            [new ComparisonDiagnosticOptionsValidator()]));

    private static void Set(ComparisonDiagnosticOptions options, string field, int value)
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
