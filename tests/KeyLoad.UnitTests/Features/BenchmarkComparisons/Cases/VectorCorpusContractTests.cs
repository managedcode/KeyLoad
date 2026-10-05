using System.Globalization;
using System.Text;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class VectorCorpusContractTests
{
    private const string IdField = "id";
    private const string NumberField = "number";
    private const string NumberFormat = "D9";
    [Test]
    [Arguments("vector-100k-exact-mixed-c16")]
    [Arguments("vector-1m-exact-mixed-c16")]
    public async Task MixedUpdatesAreUniqueAndSpreadOverTheCompleteExcludedCorpus(string id)
    {
        var profile = VectorComparisonProfile.Parse(id);
        var corpus = new VectorComparisonCorpus(profile);
        var numbers = new HashSet<int>();
        for (var ordinal = 0; ordinal < profile.UpdateCount; ordinal++)
        {
            var update = corpus.CreateUpdate(ordinal);
            var slot = (int)((1729UL + (ulong)(uint)ordinal * 2654435761UL) % (ulong)(profile.RecordCount / 10));
            await Assert.That(update.Number).IsEqualTo(9 + slot * 10);
            await Assert.That(numbers.Add(update.Number)).IsTrue();
            await Assert.That(corpus.Eligible(update.Number)).IsFalse();
        }
        await Assert.That(numbers.Max() > profile.RecordCount * 9 / 10).IsTrue();
        await Assert.That(numbers.Min() < profile.RecordCount / 10).IsTrue();
    }

    [Test]
    public async Task ExactOracleIsScaleInvariantAndMatchesIndependentCosineOrdering()
    {
        var corpus = new VectorComparisonCorpus(VectorComparisonProfile.Parse("vector-100k-exact-filtered-c16"));
        var query = corpus.CreateQueries()[0].ToArray();
        var scaled = query.Select(component => component * 8f).ToArray();
        var actual = corpus.ExactNeighborsBatch([query, scaled]);
        var expected = Enumerable.Range(0, corpus.Profile.RecordCount).Where(corpus.Eligible)
            .Select(number => Expected(corpus, number, query)).OrderBy(item => item.Distance)
            .ThenBy(item => item.Id, StringComparer.Ordinal).Take(corpus.Profile.TopK).ToArray();
        await Assert.That(actual[0].Select(item => item.Id)).IsEquivalentTo(expected.Select(item => item.Id));
        await Assert.That(actual[0].Select(item => item.Id)).IsEquivalentTo(actual[1].Select(item => item.Id));
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(Math.Abs(actual[0][index].Distance - expected[index].Distance) < 1e-12).IsTrue();
        }
    }

    [Test]
    public async Task InvalidQueryNormDimensionsAndCancellationRejectBeforeReturningAnOracle()
    {
        var corpus = new VectorComparisonCorpus(VectorComparisonProfile.Parse("vector-100k-exact-plain-c16"));
        await Assert.That(() => corpus.ExactNeighbors(new float[128])).Throws<ArgumentException>();
        await Assert.That(() => corpus.ExactNeighbors(new float[127])).Throws<ArgumentException>();
        var nonFinite = corpus.CreateQueries()[0].ToArray();
        nonFinite[0] = float.NaN;
        await Assert.That(() => corpus.ExactNeighbors(nonFinite)).Throws<ArgumentException>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.That(() => corpus.ExactNeighbors(corpus.CreateQueries()[0], cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task PayloadHasCanonicalActualFieldsAndExactly1024Utf8Bytes()
    {
        var corpus = new VectorComparisonCorpus(VectorComparisonProfile.Parse("vector-1m-exact-plain-c16"));
        var document = corpus.Create(999999);
        await Assert.That(Encoding.UTF8.GetByteCount(document.Payload)).IsEqualTo(1024);
        using var payload = System.Text.Json.JsonDocument.Parse(document.Payload);
        await Assert.That(payload.RootElement.GetProperty(IdField).GetString()).IsEqualTo(document.Id);
        await Assert.That(payload.RootElement.GetProperty(NumberField).GetInt32()).IsEqualTo(document.Number);
        await Assert.That(payload.RootElement.EnumerateObject().Count()).IsEqualTo(3);
    }

    private static VectorNeighbor Expected(VectorComparisonCorpus corpus, int number, float[] query)
    {
        var vector = corpus.CreateEmbedding(number);
        var dot = vector.Zip(query, (left, right) => (double)left * right).Sum();
        var leftNorm = vector.Sum(value => (double)value * value);
        var rightNorm = query.Sum(value => (double)value * value);
        return new("v" + number.ToString(NumberFormat, CultureInfo.InvariantCulture), 1d - dot / Math.Sqrt(leftNorm * rightNorm));
    }
}
