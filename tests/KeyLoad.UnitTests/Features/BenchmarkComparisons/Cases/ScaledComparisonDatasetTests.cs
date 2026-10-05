using System.Text;
using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaledComparisonDatasetTests
{
    private const string Expected100KDigest = "7c11c8ee7c751c133962465d876d4a54d1a55d2234d28ff3d1546826cb406607";

    [Test]
    public async Task CorpusIsIndexedLazyExactAndMatchesIndependentDigest()
    {
        var corpus = new ScaledComparisonCorpus(ScaledComparisonProfileParser.Parse("scaled-100k-c16"));
        await Assert.That(corpus.Documents.Count).IsEqualTo(100_000);
        await Assert.That(corpus.Documents.GetType().IsArray).IsFalse();
        await Assert.That(corpus.Sha256).IsEqualTo(Expected100KDigest);
        foreach (var number in new[] { 0, 1, 42, 99_999 })
        {
            var document = corpus.Documents[number];
            await Assert.That(document.Number).IsEqualTo(number);
            await Assert.That(document.Id).IsEqualTo("d" + number.ToString("D9", System.Globalization.CultureInfo.InvariantCulture));
            await Assert.That(Encoding.UTF8.GetByteCount(document.Json)).IsEqualTo(1_024);
            using var json = JsonDocument.Parse(document.Json);
            await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(4);
            await Assert.That(json.RootElement.GetProperty("number").GetInt32()).IsEqualTo(number);
            await Assert.That(document.Vector.IsEmpty).IsTrue();
        }
        await Assert.That(corpus.Documents[42].Json).IsEqualTo(corpus.CreateDocument(42).Json);
        await Assert.That(corpus.Edges.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PointAndMutationInputsAreDeterministicAndUseSeparateNumberBlocks()
    {
        var corpus = new ScaledComparisonCorpus(ScaledComparisonProfileParser.Parse("scaled-100k-c16"));
        foreach (var item in new[] { (Operation: 0, Expected: 1_729), (Operation: 1, Expected: 37_490),
                     (Operation: 2, Expected: 5_955), (Operation: 99_999, Expected: 38_576) })
        {
            await Assert.That(corpus.Input(Scenario.PointRead, 0, item.Operation, false).Number).IsEqualTo(item.Expected);
        }
        var writeWarmup = corpus.Input(Scenario.DocumentWrite, 0, 0, true);
        var writeMeasured = corpus.Input(Scenario.DocumentWrite, 0, 0, false);
        var updateMeasured = corpus.Input(Scenario.DocumentUpdate, 0, 0, false);
        var deleteMeasured = corpus.Input(Scenario.DocumentDelete, 0, 0, false);
        await Assert.That(new[] { writeWarmup.Number, writeMeasured.Number, updateMeasured.Number, deleteMeasured.Number }.Distinct().Count()).IsEqualTo(4);
        await Assert.That(writeMeasured.Number).IsEqualTo(100_256);
        await Assert.That(updateMeasured.Number).IsEqualTo(100_512);
        await Assert.That(deleteMeasured.Number).IsEqualTo(200_768);
    }

    [Test]
    public async Task EveryProfileExposesItsExactCountAndGeneratorEnforcesInputBounds()
    {
        foreach (var (id, count) in new[]
        {
            ("scaled-100k-c16", 100_000),
            ("scaled-1m-c16", 1_000_000),
            ("scaled-5m-c16", 5_000_000)
        })
        {
            var profile = ScaledComparisonProfileParser.Parse(id);
            await Assert.That(profile.Documents).IsEqualTo(count);
            await Assert.That(profile.Operations).IsEqualTo(100_000);
        }

        var corpus = new ScaledComparisonCorpus(ScaledComparisonProfileParser.Parse("scaled-100k-c16"));
        var finalPoint = corpus.Input(Scenario.PointRead, 0, 255, true).Number;
        await Assert.That(finalPoint is >= 0 and < 100_000).IsTrue();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => corpus.Input(Scenario.PointRead, 1, 0, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => corpus.Input(Scenario.PointRead, 0, 100_000, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => corpus.Input(Scenario.PointRead, 0, 256, true));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => corpus.CreateDocument(100_000));
    }
}
