namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal readonly record struct TimeSeriesIntensivePhaseResult(bool Complete, bool Succeeded, long WallTicks,
    int WorkersStarted, int PeakClientCalls, int PeakDecodedResponses, TimeSeriesIntensiveMeasurement? Measurement);
