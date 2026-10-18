namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed record ComparisonHostOutputCaptureSnapshot(ComparisonHostCaptureState State, int Length,
    bool MissingKeyLoadSetting, bool MissingQdrantSetting, bool InvalidDimensions)
{
    internal bool IsCompleted => State == ComparisonHostCaptureState.Completed;

    internal static ComparisonHostOutputCaptureSnapshot Unavailable
    {
        get;
    } =
        new(ComparisonHostCaptureState.Unavailable, 0, false, false, false);
}
