using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests;

internal sealed class DocumentWorkerSelectionTests
{
    private const string Target = "KeyLoad";
    private const int ThreeNodes = 3;
    private const int TwoNodes = 2;
    private const int MillionRecords = 1_000_000;
    private const int OneClient = 1;
    private const int TenClients = 10;
    private const int FiveHundredClients = 500;
    private const int OrdinaryClients = 16;
    private const string ScaledProfile = "scaled-1m-c16";
    private const string NativeScenario = "PointRead";

    [Test]
    [Arguments(OneClient)]
    [Arguments(TenClients)]
    [Arguments(FiveHundredClients)]
    public async Task MillionRecordIngestionPreservesActualClientCount(int clients)
    {
        var documents = new DocumentComparisonSelection(DocumentComparisonScenario.Ingest, MillionRecords, clients);
        var selected = ComparisonWorkerSelection.Read(Configuration(documents));
        await Assert.That(selected.DocumentWorkload).IsEqualTo(documents);
        await Assert.That(selected.DocumentWorkload!.Operations).IsEqualTo(MillionRecords);
        await Assert.That(selected.Options.Concurrency).IsEqualTo(OrdinaryClients);
        await Assert.That(selected.Profile).IsEqualTo(DocumentWorkerSelection.ProfileId(documents));
    }

    [Test]
    public async Task TwoNodesAndMixedFamilySelectorsRejectBeforeNativeAcquisition()
    {
        var documents = new DocumentComparisonSelection(DocumentComparisonScenario.MixedCrud, MillionRecords, OrdinaryClients);
        await Assert.That(() => ComparisonWorkerSelection.Read(Configuration(documents, TwoNodes)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => ComparisonWorkerSelection.Read(Configuration(documents, ThreeNodes,
            ComparisonWorkerSelection.ScaleProfileSetting, ScaledProfile)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => ComparisonWorkerSelection.Read(Configuration(documents, ThreeNodes,
            DocumentWorkerSelection.ClientsSetting, FiveHundredClients.ToString(CultureInfo.InvariantCulture))))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(100)]
    [Arguments(600)]
    public async Task DevelopmentIngestionRejectsIncompleteClientRangesBeforeNativeAcquisition(int operations)
    {
        var selection = new DocumentComparisonSelection(DocumentComparisonScenario.Ingest, MillionRecords, FiveHundredClients);
        var options = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.NativeDatabaseFlowFixture.ExecutionOptions;
        await Assert.That(async () => await new DocumentComparisonRunner(options).RunDevelopmentAsync(
            (_, _) => Task.FromException<IComparisonTarget>(new InvalidOperationException()), selection,
            records: operations, operations: operations, TestContext.Current!.Execution.CancellationToken))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static IConfiguration Configuration(DocumentComparisonSelection documents, int nodes = ThreeNodes,
        string? changedKey = null, string? changedValue = null)
    {
        var values = new Dictionary<string, string?>
        {
            [ComparisonWorkerSelection.TargetSetting] = Target,
            [ComparisonWorkerSelection.NodeCountSetting] = nodes.ToString(CultureInfo.InvariantCulture),
            [ComparisonWorkerSelection.ScenarioSetting] = NativeScenario,
            [ComparisonWorkerSelection.ProfileSetting] = DocumentWorkerSelection.ProfileId(documents),
            [DocumentWorkerSelection.ScenarioSetting] = documents.Scenario.ToString(),
            [DocumentWorkerSelection.RecordsSetting] = documents.DatasetRecords.ToString(CultureInfo.InvariantCulture),
            [DocumentWorkerSelection.ClientsSetting] = documents.Clients.ToString(CultureInfo.InvariantCulture)
        };
        if (changedKey is not null)
        {
            values[changedKey] = changedValue;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
