namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserBehaviorTokens
{
    public const string ChartReadyScript = "document.querySelectorAll(\'#chart .bar-row\').length===6";
    public const string SelectedProfileScript = "document.querySelector(\'#profile\')?.value";
    public const string ProvenanceScript = "document.querySelector(\'#revision-summary\')?.textContent";
    public const string VisibleField = "visible";
    public const string RetryVisibleField = "retryVisible";
    public const string RowCountField = "rowCount";
    public const string EmptyText = "";
}
