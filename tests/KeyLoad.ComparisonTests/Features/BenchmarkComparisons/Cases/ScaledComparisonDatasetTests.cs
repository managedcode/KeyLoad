using System.Text;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ScaledComparisonDatasetTests
{
    [Test]
    public async Task ActiveScaledCorpusOwnsOnlyItsParsedRecordRangeAndKeepsDocumentPayloadContract()
    {
        var profile = ScaledComparisonProfileParser.Parse("scaled-100k-c16");
        var corpus = new ScaledComparisonCorpus(profile);
        var first = corpus.Documents[0];
        var last = corpus.Documents[^1];
        await Assert.That(corpus.Documents.Count).IsEqualTo(100_000);
        await Assert.That(first.Number).IsEqualTo(0);
        await Assert.That(last.Number).IsEqualTo(99_999);
        await Assert.That(Encoding.UTF8.GetByteCount(first.Json)).IsEqualTo(profile.PayloadBytes);
        await Assert.That(first.Vector.IsEmpty).IsTrue();
        await Assert.That(() => corpus.CreateDocument(100_000)).Throws<ArgumentOutOfRangeException>();
    }
}
