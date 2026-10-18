namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveExceptionBoundary
{
    internal static bool IsNonfatal(Exception error) => error switch
    {
        OutOfMemoryException or StackOverflowException or AccessViolationException => false,
        AggregateException aggregate => aggregate.InnerExceptions.All(IsNonfatal),
        _ => error.InnerException is null || IsNonfatal(error.InnerException)
    };
}
