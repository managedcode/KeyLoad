using System.Globalization;
using System.Text;
using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class VectorProfileTests
{
    [Test]
    public async Task ClosedProfileInventoryUsesOnlyTheTwoActiveScalesAndFourDistinctAlgorithms()
    {
        await Assert.That(VectorComparisonProfile.AllIds.Count).IsEqualTo(24);
        await Assert.That(VectorComparisonProfile.AllIds.All(id => id.Contains("-100k-", StringComparison.Ordinal)
            || id.Contains("-1m-", StringComparison.Ordinal))).IsTrue();
        await Assert.That(VectorComparisonProfile.Parse("vector-1m-native-mixed-c16").IndexKind)
            .IsEqualTo(VectorIndexKind.NativeAnn);
        await Assert.That(() => VectorComparisonProfile.Parse("vector-5m-exact-plain-c16"))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => ScaledComparisonProfileParser.Parse("scaled-5m-c16"))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ProfileRoundTripPreservesExactSettingsAndRejectsModifiedCorpusSize()
    {
        var profile = VectorComparisonProfile.Parse("vector-100k-hnsw-filtered-c16");
        var json = JsonSerializer.Serialize(profile, ReportWriter.JsonOptions);
        var restored = JsonSerializer.Deserialize<VectorComparisonProfile>(json, ReportWriter.JsonOptions)!;
        await Assert.That(restored).IsEqualTo(profile);
        using var document = JsonDocument.Parse(json.Replace("100000", "5000000", StringComparison.Ordinal));
        await Assert.That(() => JsonSerializer.Deserialize<VectorComparisonProfile>(document.RootElement.GetRawText(),
            ReportWriter.JsonOptions)).Throws<JsonException>();
    }

    [Test]
    public async Task CorpusUsesCanonicalLengthDeterministicNormalizedFloatVectorsAndExcludedMixedUpdates()
    {
        var profile = VectorComparisonProfile.Parse("vector-100k-exact-mixed-c16");
        var corpus = new VectorComparisonCorpus(profile, NativeDatabaseFlowFixture.ExecutionOptions);
        var first = corpus.Create(42);
        var same = corpus.Create(42);
        await Assert.That(first.Id).IsEqualTo("v000000042");
        await Assert.That(Encoding.UTF8.GetByteCount(first.Payload)).IsEqualTo(1024);
        await Assert.That(VectorComparisonCorpus.HashVector(first.Embedding.Span))
            .IsEqualTo(VectorComparisonCorpus.HashVector(same.Embedding.Span));
        var norm = Math.Sqrt(first.Embedding.Span.ToArray().Sum(value => (double)value * value));
        await Assert.That(Math.Abs(norm - 1d) < 0.000001d).IsTrue();
        for (var ordinal = 0; ordinal < profile.UpdateCount; ordinal += 997)
        {
            var update = corpus.CreateUpdate(ordinal);
            await Assert.That(update.Number % 10).IsEqualTo(9);
            await Assert.That(corpus.Eligible(update.Number)).IsFalse();
        }
    }

    [Test]
    public async Task NativeExactOracleStreamsTheCorpusAndReturnsOrderedUniqueNeighbors()
    {
        var corpus = new VectorComparisonCorpus(VectorComparisonProfile.Parse("vector-100k-exact-filtered-c16"), NativeDatabaseFlowFixture.ExecutionOptions);
        var query = corpus.CreateQueries()[0];
        var expected = corpus.ExactNeighbors(query);
        await Assert.That(expected.Count).IsEqualTo(10);
        await Assert.That(expected.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(10);
        await Assert.That(expected.All(item => int.Parse(item.Id.AsSpan(1), CultureInfo.InvariantCulture) % 100 == 0)).IsTrue();
        await Assert.That(expected.SequenceEqual(expected.OrderBy(item => item.Distance).ThenBy(item => item.Id, StringComparer.Ordinal))).IsTrue();
    }
}
