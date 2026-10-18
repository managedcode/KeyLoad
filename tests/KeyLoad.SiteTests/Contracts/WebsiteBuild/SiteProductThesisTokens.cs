namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteProductThesisTokens
{
    internal const string AiNative = "AI-native database";
    internal const string NoDatabaseStack = "Agents shouldn't need a dozen databases";
    internal const string Future = "Why we think this is the future";
    internal const string StackRationale = "Why .NET, Orleans and ZoneTree";
    internal const string ReadmeAiNativeHeading = "## Why an " + AiNative + "?";
    internal const string ReadmeNoDatabaseStackLead = "**" + NoDatabaseStack + ".**";
    internal const string ReadmeFutureHeading = "### " + Future;
    internal const string ReadmeStackHeading = "## " + StackRationale;
    internal const string LandingMainStart = "<main>";
    internal const string LandingMainEnd = "</main>";
    internal const string UnrelatedCopy = "A fast key-value store for web sessions.";
    internal const string PartialCopy = AiNative + SiteProductCopyTokens.WordSeparator + NoDatabaseStack
        + SiteProductCopyTokens.WordSeparator + Future;

    // README and landing must state the same product theses (AC-COMP-009).
    internal static readonly string[] Theses = [AiNative, NoDatabaseStack, Future, StackRationale];

    // README theses must be real sections, so navigation links alone cannot satisfy them.
    internal static readonly string[] ReadmeTheses =
        [ReadmeAiNativeHeading, ReadmeNoDatabaseStackLead, ReadmeFutureHeading, ReadmeStackHeading];
}
