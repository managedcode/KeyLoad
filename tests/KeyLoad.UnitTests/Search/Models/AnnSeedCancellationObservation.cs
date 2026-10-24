using KeyLoad.Core.Search;

namespace KeyLoad.UnitTests.Search;

internal sealed record AnnSeedCancellationObservation(
    TimeSpan Elapsed,
    long ReadBytes,
    long ObservedReadBytes,
    bool CancellationRequested,
    AnnSeed? Seed,
    OperationCanceledException? CaptureCancellation);
