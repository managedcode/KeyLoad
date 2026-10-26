using System.Text.Json.Nodes;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Expected original files come from the authenticated current session.</summary>
internal static class SiteIsolatedGitHubArchivePaths
{
    private const string SuiteFiles = "suiteFiles";

    public static Dictionary<string, string> ExpectedWorker(JsonObject metadata)
    {
        var id = metadata[SiteIsolatedGitHubTokens.Workers]!.AsArray()[0]![SiteIsolatedGitHubTokens.Id]!.GetValue<string>();
        var prefix = "workers/" + id + "/";
        return metadata[SuiteFiles]!.AsArray().Where(file => file![SiteIsolatedGitHubTokens.Path]!.GetValue<string>()
                .StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(
            file => file![SiteIsolatedGitHubTokens.Path]!.GetValue<string>()[prefix.Length..],
            file => file![SiteIsolatedGitHubTokens.Sha256]!.GetValue<string>(), StringComparer.Ordinal);
    }
}
