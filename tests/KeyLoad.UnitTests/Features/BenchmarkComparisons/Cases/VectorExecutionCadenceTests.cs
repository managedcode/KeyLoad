using System.Globalization;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class VectorExecutionCadenceTests
{
    [Test]
    public async Task ConfiguredNativeCadenceKeepsCanonicalVectorsPayloadsAndExactOrdering()
    {
        var baselinePolicy = UnitBenchmarkOptions.Native();
        var configuredPolicy = UnitBenchmarkOptions.Native();
        configuredPolicy.Value.VectorCancellationCheckInterval = 8;
        configuredPolicy.Value.VectorYieldBatchSize = 4;
        await Assert.That(NativeComparisonExecutionOptions.Require(configuredPolicy)).IsSameReferenceAs(configuredPolicy);
        var profile = VectorComparisonProfile.Parse("vector-100k-exact-filtered-c16");
        var baseline = new VectorComparisonCorpus(profile, baselinePolicy);
        var configured = new VectorComparisonCorpus(profile, configuredPolicy);

        foreach (var number in new[] { 0, 255, 256, 4095, 4096, 99_999 })
        {
            var expected = baseline.Create(number);
            var actual = configured.Create(number);
            await Assert.That(actual.Number).IsEqualTo(expected.Number);
            await Assert.That(actual.Id).IsEqualTo(expected.Id);
            await Assert.That(actual.Payload).IsEqualTo(expected.Payload);
            await Assert.That(VectorComparisonCorpus.HashVector(actual.Embedding.Span))
                .IsEqualTo(VectorComparisonCorpus.HashVector(expected.Embedding.Span));
        }
        var query = baseline.CreateQueries()[0];
        var expectedNeighbors = baseline.ExactNeighbors(query);
        var actualNeighbors = configured.ExactNeighbors(query);
        await Assert.That(actualNeighbors.SequenceEqual(expectedNeighbors)).IsTrue();

        var evidence = new Dictionary<string, string>(StringComparer.Ordinal);
        configuredPolicy.Value.RecordEvidence(evidence);
        await Assert.That(evidence[nameof(NativeComparisonExecutionOptions.VectorCancellationCheckInterval)])
            .IsEqualTo(configuredPolicy.Value.VectorCancellationCheckInterval.ToString(CultureInfo.InvariantCulture));
        await Assert.That(evidence[nameof(NativeComparisonExecutionOptions.VectorYieldBatchSize)])
            .IsEqualTo(configuredPolicy.Value.VectorYieldBatchSize.ToString(CultureInfo.InvariantCulture));
    }
}
