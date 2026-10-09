using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-METH-001/003/004: actual production JS admits the common document inventory and rejects corrupt evidence.</summary>
internal sealed class DocumentPlanEvidenceTests
{
    private const string AcknowledgedField = "acknowledged";
    private const string CellsField = "cells";
    private const string ComparableRejectedField = "comparableRejected";
    private const string EnvelopeRejectedField = "envelopeRejected";
    private const string GroupCountsField = "groupCounts";
    private const string IngestionClientsField = "ingestionClients";
    private const string MeasuredSecondsField = "measuredSeconds";
    private const string NodesField = "nodes";
    private const string P99Field = "p99";
    private const string PerTargetField = "perTarget";
    private const string PooledLatencyMillisecondsField = "pooledLatencyMilliseconds";
    private const string PooledThroughputOperationsPerSecondField = "pooledThroughputOperationsPerSecond";
    private const string ProfilesField = "profiles";
    private const string RejectedField = "rejected";
    private const string SelectedField = "selected";
    private const string StatisticsField = "statistics";
    private const string SupportedField = "supported";
    private const string TargetsField = "targets";
    private const string ThroughputSampleStandardDeviationField = "throughputSampleStandardDeviation";
    private const string UnavailableField = "unavailable";
    private const string VariationField = "variation";
    [Test]
    public async Task AcMeth001CommonDocumentPlanHas418CellsAndOnlyOneOrThreeNodes()
    {
        var value = await ProbeAsync("plan");
        await Assert.That(value[CellsField]!.GetValue<int>()).IsEqualTo(418);
        await Assert.That(value[TargetsField]!.GetValue<int>()).IsEqualTo(11);
        await Assert.That(value[NodesField]!.AsArray().Select(item => item!.GetValue<int>())).IsEquivalentTo(new[] { 1, 3 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(value[PerTargetField]!.AsArray().All(item => item!.GetValue<int>() == 38)).IsTrue();
        await Assert.That(value[IngestionClientsField]!.AsArray().Select(item => item!.GetValue<int>())).IsEquivalentTo(new[] { 1, 10, 500 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(value[GroupCountsField]!.AsArray().Count(item => item!.GetValue<int>() == 178)).IsEqualTo(1);
        await Assert.That(value[GroupCountsField]!.AsArray().Count(item => item!.GetValue<int>() == 172)).IsEqualTo(10);
        await Assert.That(value[ProfilesField]!.AsArray().Select(item => item!.GetValue<string>())).IsEquivalentTo(
            new[] { "documents-100k-read-update50-c16", "documents-1m-read-update95-c16" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(value[SelectedField]!.GetValue<bool>()).IsTrue();
        await Assert.That(value[RejectedField]!.AsArray().All(item => item!.GetValue<bool>())).IsTrue();
    }

    [Test]
    public async Task AcMeth004PooledStatisticsRetainOperationWeightsAndVariation()
    {
        var value = await ProbeAsync("evidence");
        var statistics = value[StatisticsField]!.AsObject();
        await Assert.That(statistics[AcknowledgedField]!.GetValue<int>()).IsEqualTo(300000);
        await Assert.That(statistics[PooledThroughputOperationsPerSecondField]!.GetValue<double>()).IsEqualTo(100000);
        await Assert.That(statistics[ThroughputSampleStandardDeviationField]!.GetValue<double>()).IsEqualTo(0);
        await Assert.That(statistics[PooledLatencyMillisecondsField]![P99Field]!.GetValue<double>()).IsEqualTo(2);
        var variation = value[VariationField]!.AsObject();
        await Assert.That(variation[MeasuredSecondsField]!.GetValue<int>()).IsEqualTo(7);
        await Assert.That(variation[PooledThroughputOperationsPerSecondField]!.GetValue<double>()).IsEqualTo(42857.142857142855);
        await Assert.That(variation[ThroughputSampleStandardDeviationField]!.GetValue<double>()).IsGreaterThan(0);
    }

    [Test]
    public async Task AcMeth004EveryScheduleRetainsThreeCompleteRepetitionsAndRejectsCorruption()
    {
        var value = await ProbeAsync("evidence");
        await Assert.That(value[SupportedField]!.GetValue<int>()).IsEqualTo(38);
        await Assert.That(value[ComparableRejectedField]!.GetValue<bool>()).IsTrue();
        await Assert.That(value[UnavailableField]!.GetValue<bool>()).IsTrue();
        await Assert.That(value[EnvelopeRejectedField]!.GetValue<bool>()).IsTrue();
        await Assert.That(value[RejectedField]!.AsArray().Count).IsEqualTo(13);
        await Assert.That(value[RejectedField]!.AsArray().All(item => item!.GetValue<bool>())).IsTrue();
    }

    [Test]
    public async Task AcMeth004FailedFinalizationRetainsOriginalBytesAndRejectsOverwrite()
    {
        var value = await ProbeAsync("finalizer");
        await Assert.That(value.Count).IsEqualTo(5);
        await Assert.That(value.All(item => item.Value!.GetValue<bool>())).IsTrue();
    }

    [Test]
    public async Task AcMeth004DocumentIntakeRequiresComplete418OriginalJobAndArtifactBindings()
    {
        var value = await ProbeAsync("intake");
        await Assert.That(value[CellsField]!.GetValue<int>()).IsEqualTo(418);
        await Assert.That(value[RejectedField]!.AsArray().Count).IsEqualTo(9);
        await Assert.That(value[RejectedField]!.AsArray().All(item => item!.GetValue<bool>())).IsTrue();
    }

    private static async Task<JsonObject> ProbeAsync(string operation)
    {
        var script = Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), "tests", "KeyLoad.ComparisonTests", "Features",
            "BenchmarkComparisons", "UnitContracts", "Fixtures", "document-contract-probe.mjs");
        var result = await IsolatedAggregateNodeProcess.RunAsync([script, operation], TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEqualTo(string.Empty);
        return JsonNode.Parse(result.Output)!.AsObject();
    }
}
