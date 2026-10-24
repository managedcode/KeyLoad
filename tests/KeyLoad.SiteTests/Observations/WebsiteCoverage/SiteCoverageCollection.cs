namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal sealed class SiteCoverageCollection(string artifactRoot)
{
    public string ArtifactRoot { get; } = Path.GetFullPath(artifactRoot);
    public Dictionary<string, List<SiteCoverageScriptSnapshot>> SnapshotsBySource { get; } = new(StringComparer.Ordinal);
    public List<SiteCoverageReceipt> Receipts { get; } = [];
    public List<SiteCoverageBrowserSession> BrowserSessions { get; } = [];
    public SortedSet<string> Errors { get; } = new(StringComparer.Ordinal);
    public long BytesRead
    {
        get; set;
    }
    public int FilesRead
    {
        get; set;
    }
}
