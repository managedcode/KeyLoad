using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonArgumentTests
{
    private const string ConfigurationParameter = "configuration";
    private const string OptionsParameter = "options";
    private const string StartParameter = "start";
    private const string QueryParameter = "query";
    private const string ExpectedParameter = "expected";
    private const string DocumentParameter = "document";
    private const string TargetsParameter = "targets";
    private const string SamplesParameter = "samples";
    private const string DocumentsSetting = "Benchmarks:Documents";
    private const string TopKSetting = "Benchmarks:TopK";
    private const string ConfiguredDocumentCount = "4";
    private const string ConfiguredTopK = "2";
    private const string InvalidTopK = "5";
    private const int DocumentCount = 4;
    private const int NeighborCount = 2;
    private const int Dimensions = 2;
    private const int InvalidDimensions = 1;
    private const int PayloadBytes = 128;
    private const int GraphDepth = 2;
    private const double MeasurementSeconds = 1;

    [Test]
    public async Task AcCq007MissingConfigurationAndCorpusOptionsRejectAtThePublicBoundary()
    {
        await AssertParameterAsync(() => ComparisonOptions.Read(null!), ConfigurationParameter);
        await AssertParameterAsync(() => _ = new BenchmarkDataset(null!), OptionsParameter);
    }

    [Test]
    public async Task AcCq007RealConfigurationRetainsDefaultsValidSettingsAndBudgetRejection()
    {
        using var configuration = new ConfigurationManager();
        await Assert.That(ComparisonOptions.Read(configuration)).IsEqualTo(new ComparisonOptions());

        configuration[DocumentsSetting] = ConfiguredDocumentCount;
        configuration[TopKSetting] = ConfiguredTopK;
        var expected = new ComparisonOptions { Documents = DocumentCount, TopK = NeighborCount };
        await Assert.That(ComparisonOptions.Read(configuration)).IsEqualTo(expected);

        configuration[TopKSetting] = InvalidTopK;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ComparisonOptions.Read(configuration));
    }

    [Test]
    public async Task AcCq007MissingOracleInputsRejectEvenWhenTheActualResultIsAbsent()
    {
        var dataset = new BenchmarkDataset(SmallOptions());
        await AssertParameterAsync(() => dataset.Reachable(null!, GraphDepth), StartParameter);
        await AssertParameterAsync(() => dataset.ExactNeighbors(null!), QueryParameter);
        await AssertParameterAsync(() => BenchmarkDataset.SameDocument(null, null!), ExpectedParameter);
        await AssertParameterAsync(() => BenchmarkDataset.EventId(null!), DocumentParameter);
        await AssertParameterAsync(() => BenchmarkDataset.SameEvent(null, null!), ExpectedParameter);
    }

    [Test]
    public async Task AcCq007AbsentResultsRemainFalseAndActualCorpusResultsStillMatch()
    {
        var dataset = new BenchmarkDataset(SmallOptions());
        var document = dataset.Documents[0];
        var found = new FoundDocument(document.Id, document.Json);
        var foundEvent = new FoundEvent(BenchmarkDataset.EventId(document), 1, document.Json);

        await Assert.That(BenchmarkDataset.SameDocument(null, document)).IsFalse();
        await Assert.That(BenchmarkDataset.SameEvent(null, document)).IsFalse();
        await Assert.That(BenchmarkDataset.SameDocument(found, document)).IsTrue();
        await Assert.That(BenchmarkDataset.SameEvent(foundEvent, document)).IsTrue();
        await Assert.That(dataset.ExactNeighbors(document)[0].Id).IsEqualTo(document.Id);
        await Assert.That(new BenchmarkDataset(SmallOptions()).Sha256).IsEqualTo(dataset.Sha256);
    }

    [Test]
    public async Task AcCq007MissingTargetsRejectBeforeInvalidCorpusOptionsAreUsed()
    {
        var runner = new ComparisonRunner(SmallOptions() with { Dimensions = InvalidDimensions });
        var error = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
            runner.RunAsync(null!, null, TestContext.Current!.Execution.CancellationToken));
        await Assert.That(error!.ParamName).IsEqualTo(TargetsParameter);
    }

    [Test]
    public async Task AcCq007EmptyTargetsRetainTheExistingRejectionBeforeCorpusConstruction()
    {
        var runner = new ComparisonRunner(SmallOptions() with { Dimensions = InvalidDimensions });
        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            runner.RunAsync([], null, TestContext.Current!.Execution.CancellationToken));
        await Assert.That(error!.ParamName).IsEqualTo(TargetsParameter);
    }

    [Test]
    public async Task AcCq007StatisticsDistinguishMissingSamplesFromEmptyMeasurements()
    {
        await AssertParameterAsync(() => ComparisonRunner.Summarize(null!, MeasurementSeconds), SamplesParameter);
        var empty = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ComparisonRunner.Summarize([], MeasurementSeconds));
        await Assert.That(empty.ParamName).IsEqualTo(SamplesParameter);
    }

    private static ComparisonOptions SmallOptions() => new()
    {
        Documents = DocumentCount,
        TopK = NeighborCount,
        Dimensions = Dimensions,
        PayloadBytes = PayloadBytes,
        GraphVertices = DocumentCount,
        GraphDepth = GraphDepth
    };

    private static async Task AssertParameterAsync(Action action, string expectedParameter)
    {
        var error = Assert.ThrowsExactly<ArgumentNullException>(action);
        await Assert.That(error.ParamName).IsEqualTo(expectedParameter);
    }
}
