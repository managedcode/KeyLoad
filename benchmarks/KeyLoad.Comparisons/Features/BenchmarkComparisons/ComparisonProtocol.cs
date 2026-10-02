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
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        return deadline;
    }
}

internal static class ComparisonErrors
{
    public static string Safe(Exception error) => error is ComparisonFailureException ? error.Message
        : $"{error.GetType().Name} at {error.TargetSite?.DeclaringType?.Name}.{error.TargetSite?.Name}";
}
