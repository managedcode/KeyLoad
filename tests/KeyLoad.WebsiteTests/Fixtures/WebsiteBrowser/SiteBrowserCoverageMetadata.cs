namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed record SiteBrowserCoverageMetadata(int SchemaVersion, string SourceRevision, string BrowserVersion,
    string[] Origins, SiteBrowserCoverageSource[] Sources, string[] CoverageFiles)
{
    public static SiteBrowserCoverageMetadata Create(string revision, string browserVersion, string origin,
        SiteCoverageSourceManifest manifest)
    {
        var sources = manifest.Sources.Select(source => new SiteBrowserCoverageSource(source.Path, source.Sha256)).ToArray();
        return new(SiteCoverageTokens.Schema, revision, browserVersion,
            [origin.TrimEnd(SiteTokens.UrlPathSeparatorCharacter)], sources, []);
    }
}
