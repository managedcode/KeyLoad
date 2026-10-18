namespace KeyLoad.Comparisons;

internal static class ComparisonDeadline
{
    public static CancellationTokenSource Create(int seconds, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        return Create(TimeSpan.FromSeconds(seconds), cancellationToken: cancellationToken, timeProvider: timeProvider);
    }

    internal static CancellationTokenSource Create(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var deadline = new ComparisonCancellationSource(timeProvider, cancellationToken);
        deadline.CancelAfter(timeout);
        return deadline;
    }
}
