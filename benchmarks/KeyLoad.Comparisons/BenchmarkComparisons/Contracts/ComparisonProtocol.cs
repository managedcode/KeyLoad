namespace KeyLoad.Comparisons;

internal static class ComparisonStatuses
{
    public const string Unsupported = "unsupported";
    public const string Failed = "failed";
    public const string Measured = "measured";
}

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

internal static class ComparisonErrors
{
    private const string LocationSeparator = " at ";
    private const string SentencePeriod = ".";

    public static string Safe(Exception error) => error is ComparisonFailureException ? error.Message
        : $"{error.GetType().Name}{LocationSeparator}{error.TargetSite?.DeclaringType?.Name}{SentencePeriod}{error.TargetSite?.Name}";
}
