using System.Collections.Immutable;
namespace KeyLoad.Comparisons;

internal sealed class DocumentLatencyRecorder
{
    private readonly Dictionary<int, long> bins = [];
    private readonly System.Threading.Lock gate = new();
    private readonly DocumentHistogramContract policy = DocumentComparisonContract.Current.LatencyHistogram;
    private long count, overflow;
    private double maximum;
    internal void Record(double milliseconds)
    {
        lock (gate)
        {
            count++;
            maximum = Math.Max(maximum, milliseconds);
            if (!double.IsFinite(milliseconds) || milliseconds < DocumentMeasurementValues.NoObservedItems || milliseconds > policy.MaximumMilliseconds)
            { overflow++; return; }
            var bucket = checked((int)Math.Ceiling(milliseconds * DocumentMeasurementValues.MicrosecondsPerMillisecond / policy.ResolutionMicroseconds));
            bins[bucket] = bins.GetValueOrDefault(bucket) + DocumentMeasurementValues.SingleItemCount;
        }
    }
    internal DocumentLatencyHistogram Snapshot()
    {
        var ordered = bins.OrderBy(entry => entry.Key).ToArray();
        double Percentile(double quantile)
        {
            var threshold = (long)Math.Ceiling(count * quantile);
            long seen = DocumentMeasurementValues.NoObservedItems;
            foreach (var entry in ordered)
            {
                seen += entry.Value;
                if (seen >= threshold)
                {
                    return entry.Key * policy.ResolutionMicroseconds / DocumentMeasurementValues.MicrosecondsPerMillisecondFloatingPoint;
                }
            }
            return count == DocumentMeasurementValues.NoObservedItems ? DocumentMeasurementValues.NoObservedItems : policy.MaximumMilliseconds;
        }
        return new(policy.ResolutionMicroseconds, policy.MaximumMilliseconds, count, overflow,
            ordered.Select(entry => entry.Key).ToImmutableArray(), ordered.Select(entry => entry.Value).ToImmutableArray(),
            Percentile(DocumentMeasurementValues.MedianQuantile), Percentile(DocumentMeasurementValues.Tail95Quantile), Percentile(DocumentMeasurementValues.Tail99Quantile), maximum);
    }
}
