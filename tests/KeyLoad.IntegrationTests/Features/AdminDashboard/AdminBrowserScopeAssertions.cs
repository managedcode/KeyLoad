using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal static class AdminBrowserScopeAssertions
{
    private const string TenantId = "tenant-id";
    private const string DatabaseId = "database-id";
    private const string EmptyTenantPrefix = "dashboard-empty-";
    private const string EmptyDatabase = "empty-database";
    private const string GuidFormat = "N";
    private const string ClearRows = "clearRows";
    private const string ClearResource = "clearResource";
    private const string SubmitScope = "(()=>{const values=VALUE;for(const [id,value] of Object.entries(values)){document.getElementById(id).value=value;}document.getElementById('scope-form').requestSubmit();return {clearRows:document.querySelector('#data-table tbody').children.length===0,clearResource:!document.getElementById('resource-list').textContent.includes('dashboard-documents')};})()";
    private const string EmptyState = "document.getElementById('resource-list').textContent.includes('No matching resources on this page.')";
    private const string NoPriorData = "!document.getElementById('data-table').textContent.includes('document-1')&&document.getElementById('data-table').querySelectorAll('tbody tr').length===0&&document.getElementById('resource-list').querySelectorAll('button').length===0&&!document.getElementById('data-empty').hidden";
    private const string SelectCollections = "document.querySelector('button[data-view=collections]').click();true";
    private const string ResourcesLoaded = "document.getElementById('resource-list').textContent.includes('dashboard-documents')";
    private const string SelectResource = "document.querySelector('#resource-list button').click();true";
    private const string DocumentLoaded = "document.getElementById('data-table').textContent.includes('document-1')";

    internal static async Task VerifyEmptyScopeAndReconnectAsync(AdminBrowserCdp browser, ClusterFixture fixture,
        AdminDashboardScenario scenario, CancellationToken cancellationToken)
    {
        await browser.WaitForNetworkIdleAsync(cancellationToken);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TenantId] = EmptyTenantPrefix + Guid.NewGuid().ToString(GuidFormat),
            [DatabaseId] = EmptyDatabase
        };
        var immediate = await browser.EvaluateAsync(SubmitScope.Replace("VALUE", JsonSerializer.Serialize(values),
            StringComparison.Ordinal), cancellationToken);
        await Assert.That(immediate.GetProperty(ClearRows).GetBoolean()).IsTrue();
        await Assert.That(immediate.GetProperty(ClearResource).GetBoolean()).IsTrue();
        await browser.WaitAsync(EmptyState, cancellationToken);
        await Assert.That((await browser.EvaluateAsync(NoPriorData, cancellationToken)).GetBoolean()).IsTrue();
        await AdminBrowserAssertions.DisconnectAsync(browser, fixture, cancellationToken);
        await AdminBrowserAssertions.ConnectAsync(browser, fixture, scenario, cancellationToken);
        await browser.EvaluateAsync(SelectCollections, cancellationToken);
        await browser.WaitAsync(ResourcesLoaded, cancellationToken);
        await browser.EvaluateAsync(SelectResource, cancellationToken);
        await browser.WaitAsync(DocumentLoaded, cancellationToken);
    }
}
