using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Expected original files come from the authenticated current session aggregate.</summary>
internal static class SiteIsolatedGitHubArchivePaths
{
    private const string SuiteFiles = "suiteFiles";
    private const string EntryPath = "path";
    private const string RequestPattern = "^github/requests/request-[0-9]{5}\\.(headers|json|body|wait\\.json|execution-process\\.json|body\\.execution-process\\.json)$";
    private const string PagePattern = "^github/(jobs|artifacts)-page-[0-9]{2}\\.json$";
    private static readonly string[] ProviderFiles =
    [
        "proof.json", "github/workflow.json", "github/run-attempt.json", "github/jobs-pages.json",
        "github/artifacts-pages.json", "github/runtime/benchmark-runtime.json", "github/runtime/benchmark-runtime-files.json"
    ];

    public static HashSet<string> ExpectedSuite(JsonObject metadata)
        => new(metadata[SuiteFiles]!.AsArray().Select(file => file![EntryPath]!.GetValue<string>()), StringComparer.Ordinal);

    public static HashSet<string> ExpectedProvider() => new(ProviderFiles, StringComparer.Ordinal);

    public static HashSet<string> ParentDirectories(IEnumerable<string> files)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var end = file.IndexOf(SiteIsolatedGitHubTokens.Slash, StringComparison.Ordinal);
            while (end >= SiteIsolatedGitHubTokens.Zero)
            {
                result.Add(file[..(end + SiteIsolatedGitHubTokens.One)]);
                end = file.IndexOf(SiteIsolatedGitHubTokens.Slash, end + SiteIsolatedGitHubTokens.One, StringComparison.Ordinal);
            }
        }

        return result;
    }

    public static HashSet<string> CellIds(JsonObject metadata) => new(metadata[SiteIsolatedGitHubTokens.Workers]!
        .AsArray().Select(worker => worker![SiteIsolatedGitHubTokens.Id]!.GetValue<string>()), StringComparer.Ordinal);

    public static bool IsProviderCapture(string value, IReadOnlySet<string>? cellIds)
    {
        if (ProviderFiles.Contains(value, StringComparer.Ordinal) || value is "github/gh-api-help.txt" or "github/runtime-identity-inventory.txt")
        {
            return true;
        }

        const string prefix = "github/";
        const string suffix = "-inventory.txt";
        return Regex.IsMatch(value, RequestPattern, RegexOptions.CultureInvariant)
            || Regex.IsMatch(value, PagePattern, RegexOptions.CultureInvariant)
            || (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(suffix, StringComparison.Ordinal)
                && cellIds?.Contains(value[prefix.Length..^suffix.Length]) == true);
    }
}
