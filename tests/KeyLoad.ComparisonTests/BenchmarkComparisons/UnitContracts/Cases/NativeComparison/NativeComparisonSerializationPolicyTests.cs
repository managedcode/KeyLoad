using System.Globalization;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeComparisonSerializationPolicyTests
{
    [Test]
    public async Task NativeFactoryAdmitsOriginalReservationsAndBoundLowerValues()
    {
        var defaults = Create(static _ => { }).Value;
        await Assert.That(defaults.SurrealDbBatchRecordBuilderCapacity).IsEqualTo(12000);
        await Assert.That(defaults.SurrealDbVectorComponentBuilderCapacity).IsEqualTo(12);
        await Assert.That(defaults.PostgresVectorComponentBuilderCapacity).IsEqualTo(14);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [NativeComparisonSerializationOptions.SectionName + ":" + nameof(defaults.SurrealDbBatchRecordBuilderCapacity)] = "1",
            [NativeComparisonSerializationOptions.SectionName + ":" + nameof(defaults.SurrealDbVectorComponentBuilderCapacity)] = "1",
            [NativeComparisonSerializationOptions.SectionName + ":" + nameof(defaults.PostgresVectorComponentBuilderCapacity)] = "1"
        }).Build();
        using var lifetime = configuration as IDisposable;
        var native = new OptionsManager<NativeComparisonSerializationOptions>(new OptionsFactory<NativeComparisonSerializationOptions>(
            [new ConfigureFromConfigurationOptions<NativeComparisonSerializationOptions>(configuration.GetRequiredSection(NativeComparisonSerializationOptions.SectionName))], [],
            [new NativeComparisonSerializationOptionsValidator()]));
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        native.Value.RecordEvidence(parameters);
        await Assert.That(parameters.Count).IsEqualTo(3);
        await Assert.That(parameters[nameof(defaults.SurrealDbBatchRecordBuilderCapacity)]).IsEqualTo("1");
        await Assert.That(parameters[nameof(defaults.SurrealDbVectorComponentBuilderCapacity)]).IsEqualTo("1");
        await Assert.That(parameters[nameof(defaults.PostgresVectorComponentBuilderCapacity)]).IsEqualTo("1");
    }

    [Test]
    [Arguments(nameof(NativeComparisonSerializationOptions.SurrealDbBatchRecordBuilderCapacity), -1)]
    [Arguments(nameof(NativeComparisonSerializationOptions.SurrealDbBatchRecordBuilderCapacity), 0)]
    [Arguments(nameof(NativeComparisonSerializationOptions.SurrealDbBatchRecordBuilderCapacity), 12001)]
    [Arguments(nameof(NativeComparisonSerializationOptions.SurrealDbVectorComponentBuilderCapacity), -1)]
    [Arguments(nameof(NativeComparisonSerializationOptions.SurrealDbVectorComponentBuilderCapacity), 0)]
    [Arguments(nameof(NativeComparisonSerializationOptions.SurrealDbVectorComponentBuilderCapacity), 13)]
    [Arguments(nameof(NativeComparisonSerializationOptions.PostgresVectorComponentBuilderCapacity), -1)]
    [Arguments(nameof(NativeComparisonSerializationOptions.PostgresVectorComponentBuilderCapacity), 0)]
    [Arguments(nameof(NativeComparisonSerializationOptions.PostgresVectorComponentBuilderCapacity), 15)]
    public async Task NativeBindingRejectsInvalidReservationBeforeOwnership(string field, int value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [NativeComparisonSerializationOptions.SectionName + ":" + field] = value.ToString(CultureInfo.InvariantCulture)
        }).Build();
        using var lifetime = configuration as IDisposable;
        var native = new OptionsManager<NativeComparisonSerializationOptions>(new OptionsFactory<NativeComparisonSerializationOptions>(
            [new ConfigureFromConfigurationOptions<NativeComparisonSerializationOptions>(configuration.GetRequiredSection(NativeComparisonSerializationOptions.SectionName))], [],
            [new NativeComparisonSerializationOptionsValidator()]));
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => Task.FromResult(native.Value));
    }

    [Test]
    public async Task BothTargetConstructorsRejectPolicyBeforeTakingNativeResources()
    {
        var invalid = Create(settings => settings.SurrealDbVectorComponentBuilderCapacity = 0);
        using var http = new HttpClient();
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(async () =>
        {
            await using var target = new SurrealDbVectorTarget(http, "native-image", Guid.Empty.ToString(), UnitBenchmarkOptions.Native(), invalid);
        });
        http.CancelPendingRequests();
        // Invalid policy must be rejected before Npgsql parses this deliberately invalid connection.
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(async () =>
        {
            await using var target = new PostgresNativeVectorTarget("invalid-connection", Guid.Empty.ToString(), "native-image",
                ComparisonTopology.Standalone, UnitBenchmarkOptions.Native(), invalid, UnitBenchmarkOptions.Lifecycle());
        });
    }

    private static OptionsManager<NativeComparisonSerializationOptions> Create(Action<NativeComparisonSerializationOptions> configure)
        => new(new OptionsFactory<NativeComparisonSerializationOptions>(
            [new ConfigureNamedOptions<NativeComparisonSerializationOptions>(Options.DefaultName, configure)], [],
            [new NativeComparisonSerializationOptionsValidator()]));
}
