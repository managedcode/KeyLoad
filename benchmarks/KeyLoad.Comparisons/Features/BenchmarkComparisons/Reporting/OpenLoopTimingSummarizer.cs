namespace KeyLoad.Comparisons;

internal static class OpenLoopTimingSummarizer
{
    private const int MinimumQuantileIndex = 0;
    private const double P50Quantile = 0.50;
    private const double P95Quantile = 0.95;
    private const double P99Quantile = 0.99;
    private const double NanosecondsPerMillisecond = 1_000_000d;
    internal static OpenLoopTimingSummary Summarize(OpenLoopStateSnapshot state, int capacity)
    {
        var samples = state.Samples;
        return new(
            Quantiles(samples.Select(sample => HasObservedDisposition(sample)
                ? Delta(sample.TerminalOffsetMilliseconds, DueMilliseconds(sample)) : null), samples.Length),
            Quantiles(samples.Select(sample => Delta(sample.DecisionOffsetMilliseconds, DueMilliseconds(sample))), samples.Length),
            Quantiles(samples.Select(sample => sample.StartedOffsetMilliseconds is not null
                ? Delta(sample.StartedOffsetMilliseconds, sample.OfferedOffsetMilliseconds) : null), samples.Length),
            Quantiles(samples.Select(sample => IsNativeTerminal(sample.Outcome)
                ? Delta(sample.TerminalOffsetMilliseconds, sample.StartedOffsetMilliseconds) : null), samples.Length),
            state.ElapsedSeconds > 0 ? state.Accounting.Succeeded / state.ElapsedSeconds : 0,
            capacity, samples.Length, capacity - samples.Length);
    }

    private static bool HasObservedDisposition(OpenLoopLatencySample sample)
        => sample.Outcome is OpenLoopOutcome.NotOffered
            ? sample.DecisionOffsetMilliseconds is not null
            : sample.Outcome is not (OpenLoopOutcome.UnfinishedQueued or OpenLoopOutcome.UnfinishedStarted);

    private static bool IsNativeTerminal(OpenLoopOutcome outcome)
        => outcome is OpenLoopOutcome.Succeeded or OpenLoopOutcome.Failed or OpenLoopOutcome.TargetRejected
            or OpenLoopOutcome.TimedOutAfterStart;

    private static double DueMilliseconds(OpenLoopLatencySample sample)
        => sample.DueOffsetNanoseconds / NanosecondsPerMillisecond;

    private static OpenLoopLatencyQuantiles Quantiles(IEnumerable<double?> values, int denominator)
    {
        var ordered = values.Where(value => value is not null).Select(value => value!.Value)
            .Order().ToArray();
        return new(Percentile(ordered, P50Quantile), Percentile(ordered, P95Quantile), Percentile(ordered, P99Quantile),
            ordered.Length, denominator - ordered.Length, denominator);
    }

    private static double? Delta(double? end, double? start)
    {
        if (end is not { } later || start is not { } earlier || later < earlier)
        {
            return null;
        }
        return later - earlier;
    }

    private static double? Percentile(double[] values, double quantile)
        => values.Length == 0 ? null : values[Math.Max(MinimumQuantileIndex, (int)Math.Ceiling(values.Length * quantile) - 1)];
}
