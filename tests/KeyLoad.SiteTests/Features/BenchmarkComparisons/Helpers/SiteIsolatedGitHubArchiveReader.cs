using System.IO.Compression;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubArchiveReader
{
    public static async Task<SiteIsolatedGitHubArchiveReceipt> ExtractAsync(string capture,
        string receiptPath, JsonObject metadata, CancellationToken token)
    {
        var suitePath = ArchivePath(capture, SiteIsolatedGitHubTokens.Suite);
        var providerPath = ArchivePath(capture, SiteIsolatedGitHubTokens.Provider);
        var suiteDigest = await VerifyArchiveAsync(suitePath, SiteIsolatedGitHubTokens.Suite,
            metadata, SiteIsolatedGitHubTokens.SuiteKey, SiteIsolatedGitHubTokens.SuiteBytes, token);
        var providerDigest = await VerifyArchiveAsync(providerPath, SiteIsolatedGitHubTokens.Provider,
            metadata, SiteIsolatedGitHubTokens.ProviderKey, SiteIsolatedGitHubTokens.ProviderBytes, token);
        await using var suiteStream = File.OpenRead(suitePath);
        await using var providerStream = File.OpenRead(providerPath);
        using var suite = new ZipArchive(suiteStream, ZipArchiveMode.Read, leaveOpen: true);
        using var provider = new ZipArchive(providerStream, ZipArchiveMode.Read, leaveOpen: true);
        var suiteFiles = await SiteIsolatedGitHubArchivePreflight.InspectAsync(suite,
            SiteIsolatedGitHubArchivePaths.ExpectedSuite(metadata), provider: false, token);
        var providerFiles = await SiteIsolatedGitHubArchivePreflight.InspectAsync(provider,
            SiteIsolatedGitHubArchivePaths.ExpectedProvider(), provider: true, token,
            SiteIsolatedGitHubArchivePaths.CellIds(metadata));
        RequireAvailableDisk(capture, suiteFiles, providerFiles);
        return await SiteIsolatedGitHubArchivePublication.PublishAsync(capture, receiptPath, metadata,
            suiteFiles, providerFiles, suiteDigest, providerDigest, token);
    }

    public static async Task<SiteIsolatedGitHubArchiveFile> VerifyArchiveAsync(string path,
        string filename, JsonObject metadata, string key, long limit, CancellationToken token)
    {
        var relative = SiteIsolatedGitHubTokens.Archives + SiteIsolatedGitHubTokens.Slash + filename;
        var digest = await SiteIsolatedGitHubFileOperations.HashAsync(path, relative, limit, token);
        SiteIsolatedGitHubFileOperations.RequireArtifactIdentity(digest,
            metadata[SiteIsolatedGitHubTokens.Artifacts]![key]!);
        return digest;
    }

    private static string ArchivePath(string capture, string name) => Path.Combine(capture,
        SiteIsolatedGitHubTokens.Archives, name);

    private static void RequireAvailableDisk(string capture, SiteIsolatedGitHubArchiveEntry[] suite,
        SiteIsolatedGitHubArchiveEntry[] provider)
    {
        var required = checked(suite.Sum(entry => entry.File.Bytes) + provider.Sum(entry => entry.File.Bytes) +
            SiteIsolatedGitHubTokens.DiskReserveBytes);
        var drive = DriveInfo.GetDrives().Where(value => capture.StartsWith(
                value.Name.EndsWith(Path.DirectorySeparatorChar) ? value.Name : value.Name + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)).OrderByDescending(value => value.Name.Length).FirstOrDefault();
        if (drive is null || drive.AvailableFreeSpace < required)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Disk);
        }
    }
}
