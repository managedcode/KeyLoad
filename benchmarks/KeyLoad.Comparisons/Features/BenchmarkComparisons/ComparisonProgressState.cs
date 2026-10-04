namespace KeyLoad.Comparisons;

internal sealed class ComparisonProgressState(ComparisonProgressPhase phase, int repetition, int total,
    int completed = 0, int failed = 0)
{
    private int _completed = completed;
    private int _failed = failed;

    internal ComparisonProgressPhase Phase { get; } = phase;
    internal int Repetition { get; } = repetition;
    internal int Total { get; } = total;
    internal int Completed => Volatile.Read(ref _completed);
    internal int Failed => Volatile.Read(ref _failed);

    internal void Settle(bool success)
    {
        if (!success)
        {
            Interlocked.Increment(ref _failed);
        }
        Interlocked.Increment(ref _completed);
    }
}
