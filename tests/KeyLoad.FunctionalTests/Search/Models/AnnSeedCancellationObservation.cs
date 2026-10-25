using KeyLoad.Core.Search;

namespace KeyLoad.FunctionalTests.Search;

internal sealed record AnnSeedCancellationObservation(
    TimeSpan Elapsed,
    long ReadBytes,
    long ObservedReadBytes,
    bool CancellationRequested,
    AnnSeed? Seed,
    OperationCanceledException? CaptureCancellation);
