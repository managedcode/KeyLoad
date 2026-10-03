namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveConcurrency
{
    private int requests;
    private int responses;
    private int peakRequests;
    private int peakResponses;

    internal int PeakRequests => Volatile.Read(ref peakRequests);
    internal int PeakResponses => Volatile.Read(ref peakResponses);
    internal void EnterRequest() => UpdatePeak(ref peakRequests, Interlocked.Increment(ref requests));
    internal void ExitRequest() => Interlocked.Decrement(ref requests);
    internal void EnterResponse() => UpdatePeak(ref peakResponses, Interlocked.Increment(ref responses));
    internal void ExitResponse() => Interlocked.Decrement(ref responses);

    private static void UpdatePeak(ref int peak, int actual)
    {
        var previous = Volatile.Read(ref peak);
        while (actual > previous)
        {
            var observed = Interlocked.CompareExchange(ref peak, actual, previous);
            if (observed == previous)
            {
                return;
            }

            previous = observed;
        }
    }
}
