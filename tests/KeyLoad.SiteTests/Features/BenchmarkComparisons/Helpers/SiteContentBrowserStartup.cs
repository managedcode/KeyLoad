using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteContentBrowserStartup
{
    public static async Task<SiteBrowserSession> StartAsync(SiteContentInputs inputs, string output,
        CancellationToken token)
    {
        var browserPath = Environment.GetEnvironmentVariable(SiteBrowserTokens.BrowserEnvironment);
        var coverage = Environment.GetEnvironmentVariable(SiteBrowserTokens.CoverageEnvironment);
        if (string.IsNullOrWhiteSpace(browserPath) || string.IsNullOrWhiteSpace(coverage) ||
            !Path.IsPathFullyQualified(coverage))
        {
            throw new InvalidOperationException(SiteBrowserTokens.BrowserMissing);
        }

        var manifest = await ReadManifestAsync(inputs, coverage, token);
        await using var startup = SiteBrowserStartupResources.Create();
        startup.CreateTemporary();
        CopyTree(output, startup.Output);
        startup.StartHost();
        var profile = Path.Combine(startup.Path, SiteBrowserTokens.ChromeProfileDirectory);
        var download = Path.Combine(startup.Path, SiteBrowserTokens.DownloadDirectory);
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(download);
        var chrome = await startup.StartChromeAsync(browserPath, profile, token);
        await chrome.SetDownloadDirectoryAsync(download, token);
        var session = Path.Combine(coverage, SiteBrowserTokens.BrowserDirectory, SiteBrowserTokens.SessionsDirectory,
            SiteBrowserTokens.BrowserSessionPrefix + Guid.NewGuid().ToString("N"));
        var metadata = SiteBrowserCoverageMetadata.Create(inputs.SiteRevision, chrome.Version,
            startup.Host.BaseUrl, manifest);
        await chrome.NavigateAsync(startup.Host.BaseUrl + SiteAssetTokens.IndexHtml + SiteBrowserTokens.PageHideFragment, token);
        return startup.Transfer(session, metadata);
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(SiteCoverageTokens.InvalidSourceFailure);
            }

            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                CopyTree(entry, target);
            }
            else
            {
                File.Copy(entry, target);
            }
        }
    }

    private static async Task<SiteCoverageSourceManifest> ReadManifestAsync(SiteContentInputs inputs,
        string coverageRoot, CancellationToken token)
    {
        var path = Path.Combine(Path.GetFullPath(coverageRoot), SiteCoverageTokens.SourceManifestFile);
        var bytes = await File.ReadAllBytesAsync(path, token);
        var manifest = JsonSerializer.Deserialize<SiteCoverageSourceManifest>(bytes, SiteCoverageTokens.JsonOptions);
        var expected = SiteCoverageSourceInventory.ContentSources.Order(StringComparer.Ordinal).ToArray();
        if (manifest is null || manifest.SchemaVersion != SiteCoverageTokens.Schema ||
            manifest.SourceRevision != inputs.SiteRevision || manifest.Sources.Count != expected.Length ||
            !manifest.Sources.Select(source => source.Path).SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(SiteBrowserTokens.BrowserCoverageMismatch);
        }

        foreach (var source in manifest.Sources)
        {
            var sourcePath = SiteCoverageSourcePaths.ResolveSourcePath(inputs.Repository, source.Path);
            if (!SiteCoverageSourceManifestWriter.IsHash(source.Sha256) ||
                SiteCoverageSourceManifestWriter.Hash(await File.ReadAllBytesAsync(sourcePath, token)) != source.Sha256)
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserCoverageMismatch);
            }
        }

        return manifest;
    }
}
