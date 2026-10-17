namespace KeyLoad.Comparisons;

internal static class ComparisonStatistics
{
    private const int SingleItemCount = 1;

    public static Measurement Summarize(OperationSample[] samples, double elapsedSeconds)
    {
        const int NoItems = 0;

        if (samples.Length == NoItems || elapsedSeconds <= NoItems)
        {
            throw new ArgumentOutOfRangeException(nameof(samples));
        }

        var successes = samples.Count(sample => sample.Success);
        var queue = samples.Where(sample => sample.Queue is not null).Select(sample => sample.Queue!).ToArray();
        return new(samples.Length, successes, samples.Length - successes, elapsedSeconds, successes / elapsedSeconds,
            Percentiles(samples.Select(sample => sample.LatencyMs)),
            samples.Where(sample => sample.Success && sample.CompletedMessageId is not null).Select(sample => sample.CompletedMessageId).Distinct().Count(),
            queue.Length == NoItems ? null : Percentiles(queue.Select(item => item.EnqueueMs)),
            queue.Length == NoItems ? null : Percentiles(queue.Select(item => item.ReceiveMs)),
            queue.Length == NoItems ? null : Percentiles(queue.Select(item => item.AckMs)));
    }

    public static Latencies Percentiles(IEnumerable<double> values)
    {
        const int NoItems = 0;
        const double MedianQuantileDouble = .50;
        const double TailP95QuantileDouble = .95;
        const double PercentileIdentity = .99;

        var sorted = values.Order().ToArray();
        if (sorted.Length == NoItems)
        {
            throw new ArgumentOutOfRangeException(nameof(values));
        }

        double At(double percentile) => sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - SingleItemCount, NoItems, sorted.Length - SingleItemCount)];
        return new(At(MedianQuantileDouble), At(TailP95QuantileDouble), At(PercentileIdentity));
    }
}
