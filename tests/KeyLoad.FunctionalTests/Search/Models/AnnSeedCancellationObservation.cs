using KeyLoad.Core.Search;

namespace KeyLoad.FunctionalTests.Search;

internal sealed record AnnSeedCancellationObservation(
    TimeSpan Elapsed,
    long ObservedEntries,
    bool CancellationRequested,
    AnnSeed? Seed,
    OperationCanceledException? CaptureCancellation);
