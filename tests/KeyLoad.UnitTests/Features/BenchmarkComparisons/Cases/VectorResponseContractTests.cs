using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class VectorResponseContractTests
{
    [Test]
    public async Task ExactResponsesRequireEveryIndependentNeighborAndCorrectNativeOrdering()
    {
        var corpus = Corpus("vector-100k-exact-filtered-c16");
        var expected = corpus.ExactNeighbors(corpus.CreateQueries()[0]);
        await Assert.That(VectorResponseValidator.CalculateRecall(corpus, expected, expected)).IsEqualTo(1d);
        var reversed = expected.Reverse().ToArray();
        await Assert.That(() => VectorResponseValidator.CalculateRecall(corpus, reversed, expected)).Throws<InvalidDataException>();
        await Assert.That(() => VectorResponseValidator.CalculateRecall(corpus, expected.Take(9).ToArray(), expected))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task NativeUnknownDuplicateIneligibleAndNonFiniteNeighborsAreRejected()
    {
        var corpus = Corpus("vector-100k-hnsw-filtered-c16");
        var expected = corpus.ExactNeighbors(corpus.CreateQueries()[0]);
        foreach (var replacement in new[]
        {
            new VectorNeighbor("v999999999", expected[0].Distance),
            new VectorNeighbor("x000000000", expected[0].Distance),
            new VectorNeighbor("v000000001", expected[0].Distance),
            new VectorNeighbor(expected[0].Id, double.NaN),
            new VectorNeighbor(expected[0].Id, -1d),
            new VectorNeighbor(expected[0].Id, 3d),
            expected[1]
        })
        {
            var invalid = expected.ToArray();
            invalid[0] = replacement;
            await Assert.That(() => VectorResponseValidator.CalculateRecall(corpus, invalid, expected))
                .Throws<InvalidDataException>();
        }
    }

    [Test]
    public async Task ApproximateRecallIsTheActualIntersectionAndLowIndividualRecallIsRetainedForAggregation()
    {
        var corpus = Corpus("vector-100k-hnsw-plain-c16");
        var expected = corpus.ExactNeighbors(corpus.CreateQueries()[0]);
        var present = expected.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var outside = corpus.StreamDocuments().First(item => !present.Contains(item.Id));
        var approximate = expected.Take(9).Append(new VectorNeighbor(outside.Id, 2d)).ToArray();
        await Assert.That(VectorResponseValidator.CalculateRecall(corpus, approximate, expected)).IsEqualTo(0.9d);
        var exact = Corpus("vector-100k-exact-plain-c16");
        await Assert.That(() => VectorResponseValidator.CalculateRecall(exact, approximate, expected))
            .Throws<InvalidDataException>();
    }

    private static VectorComparisonCorpus Corpus(string id) => new(VectorComparisonProfile.Parse(id));
}
