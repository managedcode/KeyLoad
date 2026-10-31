using BenchmarkDotNet.Attributes;
using KeyLoad.Query.Search;

namespace KeyLoad.BenchmarkScenarios.BenchmarkComparisons;

/// <summary>Measures the production SIMD cosine kernels with computed and retained candidate norms.</summary>
[MemoryDiagnoser]
[InProcess]
[WarmupCount(3)]
[IterationCount(6)]
[IterationTime(200)]
public class CosineMetricBenchmarks
{
    private const int CorpusPhaseOffset = 7;
    private const string UnequalScores = "The production cosine kernels disagree.";
    private float[] query = [];
    private float[] candidate = [];
    private double queryNorm;
    private double candidateNorm;

    /// <summary>Gets or sets the short, wide-tail or maximum supported vector dimension.</summary>
    [Params(16, 769, 4_096)]
    public int Dimension { get; set; }

    /// <summary>Prepares owned finite vectors and verifies identical production scores before timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        SimdRuntime.RequireHardwareSupport();
        query = Enumerable.Range(0, Dimension).Select(index => (float)Math.Sin(index)).ToArray();
        candidate = Enumerable.Range(0, Dimension).Select(index => (float)Math.Sin(index + CorpusPhaseOffset)).ToArray();
        queryNorm = SimilarityMetricMath.Norm(query);
        candidateNorm = SimilarityMetricMath.Norm(candidate);
        if (RecomputeCandidateNorm() != RetainedCandidateNorm())
        {
            throw new InvalidOperationException(UnequalScores);
        }
    }

    /// <summary>Computes the candidate norm in the exact metric's existing combined SIMD pass.</summary>
    /// <returns>The cosine score.</returns>
    [Benchmark(Baseline = true)]
    public double RecomputeCandidateNorm()
        => SimilarityMetricMath.Score(query, candidate, DistanceMetric.Cosine, queryNorm);

    /// <summary>Uses the norm retained by the immutable packed ANN vector arena.</summary>
    /// <returns>The cosine score with unchanged double-precision reduction grouping.</returns>
    [Benchmark]
    public double RetainedCandidateNorm()
        => SimilarityMetricMath.CosineWithNorms(query, candidate, queryNorm, candidateNorm);
}
