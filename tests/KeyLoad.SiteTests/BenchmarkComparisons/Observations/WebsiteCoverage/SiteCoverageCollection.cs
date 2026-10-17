namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteCoverageCollection
{
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
