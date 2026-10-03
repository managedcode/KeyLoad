namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveAttemptLedger
{
    private readonly TimeSeriesIntensiveAttempt[] storage;
    private readonly int[] states;
    private readonly int offset;
    private readonly int repetition;

    internal TimeSeriesIntensiveAttemptLedger(TimeSeriesIntensiveAttempt[] storage, int offset, int count, int repetition)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, TimeSeriesIntensiveProfile.OperationCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, storage.Length - count);
        ArgumentOutOfRangeException.ThrowIfNegative(repetition);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(repetition, TimeSeriesIntensiveProfile.RepetitionCount);
        this.storage = storage;
        this.offset = offset;
        this.repetition = repetition;
        states = new int[count];
        for (var index = 0; index < count; index++)
        {
            storage[offset + index] = new(repetition, index, index % TimeSeriesIntensiveProfile.Concurrency,
                0, 0, TimeSeriesIntensiveOutcome.NotStarted, null, default, 0, default);
        }
    }

    internal ReadOnlyMemory<TimeSeriesIntensiveAttempt> Attempts => storage.AsMemory(offset, states.Length);
    internal bool Succeeded
    {
        get
        {
            if (!Complete)
            {
                return false;
            }

            foreach (var attempt in Attempts.Span)
            {
                if (attempt.Outcome != TimeSeriesIntensiveOutcome.Succeeded)
                {
                    return false;
                }
            }

            return true;
        }
    }
    internal bool Complete => states.All(state => state == TimeSeriesIntensiveRuntimePolicy.Published);

    internal void Publish(TimeSeriesIntensiveAttempt attempt)
    {
        if (attempt.Repetition != repetition || attempt.Index < 0 || attempt.Index >= states.Length
            || attempt.Worker != attempt.Index % TimeSeriesIntensiveProfile.Concurrency || !Enum.IsDefined(attempt.Outcome)
            || attempt.Outcome == TimeSeriesIntensiveOutcome.NotStarted || attempt.LatencyTicks < 0 || attempt.ValidationTicks < 0
            || (attempt.Outcome == TimeSeriesIntensiveOutcome.Succeeded
                && (attempt.Failure != default || !attempt.ResultCount.HasValue || attempt.ResultCount < 0)))
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.InvalidAttempt);
        }

        if (Interlocked.CompareExchange(ref states[attempt.Index], 1, 0) != 0)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.DuplicateAttempt);
        }

        storage[offset + attempt.Index] = attempt;
        Volatile.Write(ref states[attempt.Index], TimeSeriesIntensiveRuntimePolicy.Published);
    }

    internal void ValidateComplete()
    {
        if (!Complete)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.MissingAttempt);
        }
    }

    internal static TimeSeriesIntensiveAttempt[] CreateMeasuredStorage()
    {
        var storage = new TimeSeriesIntensiveAttempt[TimeSeriesIntensiveRuntimePolicy.TotalAttempts];
        for (var slot = 0; slot < storage.Length; slot++)
        {
            var index = slot % TimeSeriesIntensiveProfile.OperationCount;
            storage[slot] = new(slot / TimeSeriesIntensiveProfile.OperationCount, index, index % TimeSeriesIntensiveProfile.Concurrency,
                0, 0, TimeSeriesIntensiveOutcome.NotStarted, null, default, 0, default);
        }

        return storage;
    }
}
