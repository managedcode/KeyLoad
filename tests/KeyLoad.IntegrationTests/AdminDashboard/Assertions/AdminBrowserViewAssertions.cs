namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

/// <summary>AC-AD-009/AC-AD-010: the redesigned console views render real RF3 observations in a real browser.</summary>
internal static class AdminBrowserViewAssertions
{
    private const string SelectView = "document.querySelector('button[data-view=VIEW]').click();true";
    private const string ViewToken = "VIEW";
    private const string Current = "document.querySelector('button[data-view=VIEW]').getAttribute('aria-current')==='page'";
    private const string Overview = "overview";
    private const string Catalog = "catalog";
    private const string Errors = "errors";
    private const string Cluster = "cluster";
    private const string Storage = "storage";
    private const string Performance = "performance";
    private const string ActivityChart = "document.querySelector('#activity-chart svg .series path')!==null"
        + "&&document.querySelectorAll('#spark-throughput path').length>0";
    private const string CatalogRows = "document.getElementById('catalog-table').textContent.includes('dashboard-documents')"
        + "&&document.getElementById('catalog-table').textContent.includes('dashboard-jobs')"
        + "&&document.querySelectorAll('#catalog-chips button').length>=3";
    private const string EventLog = "document.getElementById('event-log').textContent.includes('Connected to node')";
    private const string Topology = "document.querySelectorAll('#topology .node').length===3"
        + "&&document.querySelectorAll('#topology .node.self').length===1";
    private const string DiskRows = "document.querySelectorAll('#disk-table tbody tr').length>0"
        + "&&document.querySelectorAll('#disk-bar i').length>0";
    private const string PerformanceStats = "document.querySelectorAll('#perf-stats .stat').length===6"
        + "&&document.querySelectorAll('#perf-admission .meter-track').length===4";

    internal static async Task VerifyViewsAsync(AdminBrowserCdp browser, CancellationToken cancellationToken)
    {
        await OpenAsync(browser, Overview, ActivityChart, cancellationToken);
        await OpenAsync(browser, Performance, PerformanceStats, cancellationToken);
        await OpenAsync(browser, Catalog, CatalogRows, cancellationToken);
        await OpenAsync(browser, Errors, EventLog, cancellationToken);
        await OpenAsync(browser, Cluster, Topology, cancellationToken);
        await OpenAsync(browser, Storage, DiskRows, cancellationToken);
        await OpenAsync(browser, Overview, ActivityChart, cancellationToken);
    }

    private static async Task OpenAsync(AdminBrowserCdp browser, string view, string ready, CancellationToken cancellationToken)
    {
        await browser.EvaluateAsync(SelectView.Replace(ViewToken, view, StringComparison.Ordinal), cancellationToken);
        await browser.WaitAsync(ready, cancellationToken);
        await Assert.That((await browser.EvaluateAsync(Current.Replace(ViewToken, view, StringComparison.Ordinal),
            cancellationToken)).GetBoolean()).IsTrue();
    }
}
