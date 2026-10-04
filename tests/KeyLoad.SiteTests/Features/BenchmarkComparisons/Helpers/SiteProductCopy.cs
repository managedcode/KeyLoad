using System.Net;
using System.Text.RegularExpressions;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Reads public product copy from the actual repository and compares its visible text.</summary>
internal static partial class SiteProductCopy
{
    internal static Task<string> ReadSourceAsync(string relativePath)
    {
        var repository = Environment.GetEnvironmentVariable(SiteTokens.RepositoryEnvironment);
        if (string.IsNullOrWhiteSpace(repository) || !Path.IsPathFullyQualified(repository))
        {
            throw new InvalidOperationException(SiteProductCopyTokens.MissingRepository);
        }

        return File.ReadAllTextAsync(Path.Combine(repository, relativePath),
            TestContext.Current!.Execution.CancellationToken);
    }

    internal static string ReadSection(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = start < 0 ? -1 : source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            throw new InvalidDataException(SiteProductCopyTokens.MissingSection);
        }

        return source[start..end];
    }

    internal static string PlainText(string source)
    {
        var plainText = WebUtility.HtmlDecode(HtmlTags().Replace(source, SiteProductCopyTokens.WordSeparator));
        return string.Join(SiteProductCopyTokens.WordSeparator,
            plainText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    internal static string[] Missing(string source, IEnumerable<string> contracts)
    {
        var text = PlainText(source);
        return [.. contracts.Where(contract => !text.Contains(contract, StringComparison.OrdinalIgnoreCase))];
    }

    [GeneratedRegex(SiteProductCopyTokens.HtmlTagPattern, RegexOptions.CultureInvariant,
        SiteProductCopyTokens.PatternTimeoutMilliseconds)]
    private static partial Regex HtmlTags();
}
