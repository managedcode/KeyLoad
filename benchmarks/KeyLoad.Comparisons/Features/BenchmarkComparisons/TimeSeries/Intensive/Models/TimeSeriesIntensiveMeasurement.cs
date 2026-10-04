namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensiveMeasurement(int Attempted, int Succeeded, double WallSeconds,
    double Throughput, double ValidationWorkerSeconds, double P50Milliseconds, double P95Milliseconds,
    double P99Milliseconds)
{
    internal bool Publishable => Attempted == TimeSeriesIntensiveProfile.OperationCount && Succeeded == Attempted;
}
