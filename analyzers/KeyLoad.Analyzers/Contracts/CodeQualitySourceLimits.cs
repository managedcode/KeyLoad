namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class CodeQualitySourceLimits
{
    public const int DiagnosticExcerptLength = 96;
    private const int TruncationEllipsisLength = 3;
    public const int DiagnosticExcerptPrefixLength =
        DiagnosticExcerptLength - TruncationEllipsisLength;
}
