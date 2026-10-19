using System.Globalization;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonSerializationPolicyTests
{
    [Test]
    public async Task FactoryAdmitsOriginalReservationsAndBoundLowerValues()
    {
        var defaults = Create(static _ => { }).Value;
        await Assert.That(defaults.SurrealDbBatchRecordBuilderCapacity).IsEqualTo(12000);
        await Assert.That(defaults.SurrealDbVectorComponentBuilderCapacity).IsEqualTo(12);
        await Assert.That(defaults.PostgresVectorComponentBuilderCapacity).IsEqualTo(14);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ComparisonSerializationOptions.SectionName + ":" + nameof(defaults.SurrealDbBatchRecordBuilderCapacity)] = "1",
            [ComparisonSerializationOptions.SectionName + ":" + nameof(defaults.SurrealDbVectorComponentBuilderCapacity)] = "1",
            [ComparisonSerializationOptions.SectionName + ":" + nameof(defaults.PostgresVectorComponentBuilderCapacity)] = "1"
        }).Build();
        using var lifetime = configuration as IDisposable;
        var execution = new OptionsManager<ComparisonSerializationOptions>(new OptionsFactory<ComparisonSerializationOptions>(
            [new ConfigureFromConfigurationOptions<ComparisonSerializationOptions>(configuration.GetRequiredSection(ComparisonSerializationOptions.SectionName))], [],
            [new ComparisonSerializationOptionsValidator()]));
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        execution.Value.RecordEvidence(parameters);
        await Assert.That(parameters.Count).IsEqualTo(3);
        await Assert.That(parameters[nameof(defaults.SurrealDbBatchRecordBuilderCapacity)]).IsEqualTo("1");
        await Assert.That(parameters[nameof(defaults.SurrealDbVectorComponentBuilderCapacity)]).IsEqualTo("1");
        await Assert.That(parameters[nameof(defaults.PostgresVectorComponentBuilderCapacity)]).IsEqualTo("1");
    }

    [Test]
    [Arguments(nameof(ComparisonSerializationOptions.SurrealDbBatchRecordBuilderCapacity), -1)]
    [Arguments(nameof(ComparisonSerializationOptions.SurrealDbBatchRecordBuilderCapacity), 0)]
    [Arguments(nameof(ComparisonSerializationOptions.SurrealDbBatchRecordBuilderCapacity), 12001)]
    [Arguments(nameof(ComparisonSerializationOptions.SurrealDbVectorComponentBuilderCapacity), -1)]
    [Arguments(nameof(ComparisonSerializationOptions.SurrealDbVectorComponentBuilderCapacity), 0)]
    [Arguments(nameof(ComparisonSerializationOptions.SurrealDbVectorComponentBuilderCapacity), 13)]
    [Arguments(nameof(ComparisonSerializationOptions.PostgresVectorComponentBuilderCapacity), -1)]
    [Arguments(nameof(ComparisonSerializationOptions.PostgresVectorComponentBuilderCapacity), 0)]
    [Arguments(nameof(ComparisonSerializationOptions.PostgresVectorComponentBuilderCapacity), 15)]
    public async Task BindingRejectsInvalidReservationBeforeOwnership(string field, int value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ComparisonSerializationOptions.SectionName + ":" + field] = value.ToString(CultureInfo.InvariantCulture)
        }).Build();
        using var lifetime = configuration as IDisposable;
        var execution = new OptionsManager<ComparisonSerializationOptions>(new OptionsFactory<ComparisonSerializationOptions>(
            [new ConfigureFromConfigurationOptions<ComparisonSerializationOptions>(configuration.GetRequiredSection(ComparisonSerializationOptions.SectionName))], [],
            [new ComparisonSerializationOptionsValidator()]));
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => Task.FromResult(execution.Value));
    }

    [Test]
    public async Task BothTargetConstructorsRejectPolicyBeforeTakingResources()
    {
        var invalid = Create(settings => settings.SurrealDbVectorComponentBuilderCapacity = 0);
        using var http = new HttpClient();
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(async () =>
        {
            await using var target = new SurrealDbVectorTarget(http, "execution-image", Guid.Empty.ToString(), UnitBenchmarkOptions.Execution(), invalid);
        });
        http.CancelPendingRequests();
        // Invalid policy must be rejected before Npgsql parses this deliberately invalid connection.
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(async () =>
        {
            await using var target = new PostgresVectorTarget("invalid-connection", Guid.Empty.ToString(), "execution-image",
                ComparisonTopology.Standalone, UnitBenchmarkOptions.Execution(), invalid, UnitBenchmarkOptions.Lifecycle());
        });
    }

    private static OptionsManager<ComparisonSerializationOptions> Create(Action<ComparisonSerializationOptions> configure)
        => new(new OptionsFactory<ComparisonSerializationOptions>(
            [new ConfigureNamedOptions<ComparisonSerializationOptions>(Options.DefaultName, configure)], [],
            [new ComparisonSerializationOptionsValidator()]));
}
