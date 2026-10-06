namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ScaleServerCancellationProbeSettlement
{
    internal static async Task CaptureAsync(Task original, List<Exception> failures)
    {
        try
        { await original; }
        catch (Exception failure) when (original.IsFaulted || original.IsCanceled)
        {
            if (original.Exception is { } aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                { AddDistinct(failures, inner); }
            }
            else
            { AddDistinct(failures, failure); }
        }
    }

    private static void AddDistinct(List<Exception> failures, Exception failure)
    {
        if (!failures.Any(existing => ReferenceEquals(existing, failure)))
        { failures.Add(failure); }
    }
}
