using System.IO.Compression;

namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal sealed record SiteIsolatedGitHubArchiveEntry(ZipArchiveEntry Entry, SiteIsolatedGitHubArchiveFile File);
