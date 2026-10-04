namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteProductCopyTokens
{
    internal const string MissingRepository = "Product copy tests require the actual site repository path.";
    internal const string MissingSection = "The public product copy section is missing or incomplete.";
    internal const string HtmlTagPattern = "<[^>]*>";
    internal const string WordSeparator = " ";
    internal const int PatternTimeoutMilliseconds = 1000;
}
