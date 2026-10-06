using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubArchivePaths
{
    private const string ProfileField = "profile";
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
        var result = new HashSet<string>(StringComparer.Ordinal) { SiteIsolatedGitHubTokens.Manifest, "cohort-receipt.json" };
        foreach (var profile in Profiles())
        {
            result.Add(FamilyRoot(profile) + SiteIsolatedGitHubTokens.Manifest);
        }
        foreach (var worker in metadata[SiteIsolatedGitHubTokens.Workers]!.AsArray())
        {
            var entry = worker ?? throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidReceipt);
            var root = FamilyRoot(entry[ProfileField]!.GetValue<string>());
            var directory = root + "workers/" + entry[SiteIsolatedGitHubTokens.Id]!.GetValue<string>() + "/";
            result.Add(directory + SiteIsolatedGitHubTokens.Raw);
            if (root.Length > 0)
            {
                result.Add(directory + "server-resource-evidence.json");
            }
        }

        return result;
    }

    public static HashSet<string> ExpectedProvider()
    {
        var result = new HashSet<string>(ProviderFiles, StringComparer.Ordinal) { "composite-plan.json", "plans/intensive-1k-c16.json" };
        foreach (var profile in Profiles())
        {
            result.Add("plans/" + profile + ".json");
            result.Add(FamilyRoot(profile) + "proof.json");
        }
        return result;
    }

    private static IEnumerable<string> Profiles()
    {
        foreach (var scale in new[] { "100k", "1m" })
        {
            yield return "scaled-" + scale + "-c16";
            foreach (var method in new[] { "exact", "hnsw", "ivfflat", "native" })
            {
                foreach (var mode in new[] { "plain", "filtered", "mixed" })
                {
                    yield return "vector-" + scale + "-" + method + "-" + mode + "-c16";
                }
            }
        }
    }

    private static string FamilyRoot(string profile) => profile == "intensive-1k-c16" ? string.Empty
        : (profile.StartsWith("vector-", StringComparison.Ordinal) ? "vector/" : "scaled/") + profile + "/";

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
