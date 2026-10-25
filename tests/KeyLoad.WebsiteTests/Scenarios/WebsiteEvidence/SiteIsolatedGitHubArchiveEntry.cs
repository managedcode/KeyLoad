using System.IO.Compression;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed record SiteIsolatedGitHubArchiveEntry(ZipArchiveEntry Entry, SiteIsolatedGitHubArchiveFile File);
