namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteUnsupportedProducerCandidate(
    long RunId,
    int Attempt,
    string SourceRevision,
    string Event,
    string Conclusion);
