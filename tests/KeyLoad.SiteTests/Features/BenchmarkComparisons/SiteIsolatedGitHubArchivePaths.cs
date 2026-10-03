using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubArchivePaths
{
    private const string PagePattern = "^(jobs|artifacts)-page-([0-9]{2})\\.json$";
    private const string RequestPattern = "^requests/request-[0-9]{5}\\.(headers|json|body|wait\\.json)$";
    private static readonly string[] ProviderFiles =
    [
        SiteIsolatedGitHubTokens.Proof,
        SiteIsolatedGitHubTokens.ImageProof,
        ImagePath(SiteIsolatedGitHubTokens.Bundle),
        ImagePath(SiteIsolatedGitHubTokens.ImageReceipt),
        ImagePath(SiteIsolatedGitHubTokens.ServerManifest),
        ImagePath(SiteIsolatedGitHubTokens.ComparisonsManifest),
    ];

    public static HashSet<string> ExpectedSuite(JsonObject metadata)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { SiteIsolatedGitHubTokens.Manifest };
        foreach (var worker in metadata[SiteIsolatedGitHubTokens.Workers]!.AsArray())
        {
            result.Add(string.Join(SiteIsolatedGitHubTokens.Slash, SiteIsolatedGitHubTokens.Workers,
                worker![SiteIsolatedGitHubTokens.Id]!.GetValue<string>(), SiteIsolatedGitHubTokens.Raw));
        }

        return result;
    }

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
        if (ProviderFiles.Contains(value, StringComparer.Ordinal) ||
            value is SiteIsolatedGitHubFields.Workflow or SiteIsolatedGitHubFields.NativeRun or
                SiteIsolatedGitHubFields.Jobs or SiteIsolatedGitHubFields.ArtifactPages or
                SiteIsolatedGitHubFields.GhHelp or SiteIsolatedGitHubFields.ImageInventory)
        {
            return true;
        }

        var page = Regex.Match(value, PagePattern, RegexOptions.CultureInvariant);
        if (page.Success)
        {
            var number = int.Parse(page.Groups[SiteIsolatedGitHubArchiveNumbers.PageGroup].Value, CultureInfo.InvariantCulture);
            return number is >= SiteIsolatedGitHubTokens.One and <= SiteIsolatedGitHubArchiveNumbers.Pages;
        }

        return Regex.IsMatch(value, RequestPattern, RegexOptions.CultureInvariant) ||
               (value.EndsWith(SiteIsolatedGitHubFields.ZipInventorySuffix, StringComparison.Ordinal) &&
                cellIds?.Contains(value[..^SiteIsolatedGitHubFields.ZipInventorySuffix.Length]) == true);
    }

    private static string ImagePath(string file) => SiteIsolatedGitHubTokens.Images + SiteIsolatedGitHubTokens.Slash + file;
}

internal static class SiteIsolatedGitHubArchiveNumbers
{
    public const int PageGroup = 2;
    public const int Pages = 20;
}
