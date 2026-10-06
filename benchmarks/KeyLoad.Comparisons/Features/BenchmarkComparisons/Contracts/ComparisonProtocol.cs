namespace KeyLoad.Comparisons;

internal static class ComparisonStatuses
{
    public const string Unsupported = "unsupported";
    public const string Failed = "failed";
    public const string Measured = "measured";
}

internal static class ComparisonDeadline
{
    public static CancellationTokenSource Create(int seconds, CancellationToken cancellationToken)
    {
        return Create(TimeSpan.FromSeconds(seconds), cancellationToken);
    }

    internal static CancellationTokenSource Create(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
