using System.Collections;
using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveReceiptView : IReadOnlyList<TimeSeriesIntensiveAppendReceipt>
{
    private readonly ImmutableArray<Guid> commands;
    private readonly ReadOnlyMemory<TimeSeriesIntensiveAttempt> attempts;

    internal TimeSeriesIntensiveReceiptView(ImmutableArray<Guid> commands, ReadOnlyMemory<TimeSeriesIntensiveAttempt> attempts)
    {
        if (commands.IsDefault || commands.Length != attempts.Length)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.UnvalidatedReceipt);
        }

        this.commands = commands;
        this.attempts = attempts;
    }

    public int Count => attempts.Length;

    public TimeSeriesIntensiveAppendReceipt this[int index]
    {
        get
        {
            var attempt = attempts.Span[index];
            if (attempt.Outcome != TimeSeriesIntensiveOutcome.Succeeded || attempt.ReceiptSequence < 1
                || attempt.ReceiptSequence > Count)
            {
                throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.UnvalidatedReceipt);
            }

            return new(commands[index], attempt.ReceiptSequence);
        }
    }

    public IEnumerator<TimeSeriesIntensiveAppendReceipt> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
