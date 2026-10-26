using System.IO.Compression;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteIsolatedGitHubArchivePreflight
{
    internal static void ValidatePath(string path, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(path) || path.Contains(SiteIsolatedGitHubTokens.Backslash, StringComparison.Ordinal) ||
            Path.IsPathRooted(path) || path.Split(SiteIsolatedGitHubTokens.Slash).Any(part =>
                part is SiteIsolatedGitHubTokens.Dot or SiteIsolatedGitHubTokens.Parent))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
        }

        if (!seen.Add(path))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Duplicate);
        }
    }

    internal static void ValidateType(ZipArchiveEntry entry, bool directory)
    {
        var unix = (entry.ExternalAttributes >> SiteIsolatedGitHubTokens.UnixShift) & SiteIsolatedGitHubTokens.UnixMask;
        var allowed = directory ? SiteIsolatedGitHubTokens.UnixDirectory : SiteIsolatedGitHubTokens.UnixRegular;
        var attributes = (FileAttributes)entry.ExternalAttributes;
        if ((unix != SiteIsolatedGitHubTokens.Zero && unix != allowed) ||
            (attributes & FileAttributes.ReparsePoint) != SiteIsolatedGitHubTokens.Zero ||
            (!directory && (attributes & FileAttributes.Directory) != SiteIsolatedGitHubTokens.Zero))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
        }
    }

}
