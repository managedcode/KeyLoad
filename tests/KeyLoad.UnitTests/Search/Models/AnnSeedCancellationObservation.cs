using KeyLoad.Core.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed record AnnSeedCancellationObservation(
    TimeSpan Elapsed,
    long ReadBytes,
    long ObservedReadBytes,
    bool CancellationRequested,
    AnnSeed? Seed,
    OperationCanceledException? CaptureCancellation);
