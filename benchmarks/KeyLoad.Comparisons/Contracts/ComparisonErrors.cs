namespace KeyLoad.Comparisons;

internal static class ComparisonErrors
{
    private const string LocationSeparator = " at ";
    private const string SentencePeriod = ".";

    public static string Safe(Exception error) => error is ComparisonFailureException ? error.Message
        : $"{error.GetType().Name}{LocationSeparator}{error.TargetSite?.DeclaringType?.Name}{SentencePeriod}{error.TargetSite?.Name}";
}
