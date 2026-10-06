using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AdminDashboardBrowserTests(ClusterFixture fixture)
{
    private const string AdminPath = "/admin";
    private const string Ready = "document.readyState==='complete'&&!!document.getElementById('connect-form')";
    private const string SelectCollections = "document.querySelector('button[data-view=collections]').click();true";
    private const string ResourcesLoaded = "document.querySelector('#resource-list')?.textContent.includes('dashboard-documents')===true";
    private const string SelectResource = "(()=>{const button=[...document.querySelectorAll('#resource-list button')].find(item=>item.textContent.includes('dashboard-documents'));button.click();return true;})()";
    private const string RowsLoaded = "document.querySelector('#data-title')?.textContent==='dashboard-documents'"
        + "&&document.querySelectorAll('#data-table tbody tr').length===1"
        + "&&document.querySelector('#data-table')?.textContent.includes('document-1')===true";
    private const string InjectionAbsent = "window.dashboardInjection!==true";
    private const string FocusVisible = "(()=>{const refresh=document.getElementById('refresh');refresh.focus();return document.activeElement===refresh;})()";

    [Test]
    public async Task AcAd006RealChromeBrowsesSeededRf3DataAndDisconnectClearsSession()
    {
        using var deadlineTimeout = new CancellationTokenSource(AdminBrowserProtocol.Deadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        var scenario = await AdminDashboardScenario.CreateAsync(fixture, deadline.Token);
        using var http = fixture.App.CreateHttpClient(McpCallerProtocol.Node1, McpCallerProtocol.HttpEndpoint);
        var url = new Uri(http.BaseAddress!, AdminPath).AbsoluteUri;
        await using var browser = await AdminBrowserProcess.StartAsync(deadline.Token);
        await AdminBrowserLifecycleAssertions.BeginNetworkAsync(browser.Cdp, deadline.Token);
        await browser.Cdp.CommandAsync(AdminBrowserProtocol.PageNavigate, new { url }, deadline.Token);
        await browser.Cdp.WaitAsync(Ready, deadline.Token);
        await AdminBrowserAssertions.ConnectAsync(browser.Cdp, fixture, scenario, deadline.Token);
        await AdminBrowserLifecycleAssertions.VerifyRefreshAndMotionAsync(browser.Cdp, deadline.Token);
        await AdminBrowserLifecycleAssertions.VerifyBackgroundSuspensionAsync(browser.Cdp, deadline.Token);
        await browser.Cdp.EvaluateAsync(SelectCollections, deadline.Token);
        await browser.Cdp.WaitAsync(ResourcesLoaded, deadline.Token);
        await browser.Cdp.EvaluateAsync(SelectResource, deadline.Token);
        await browser.Cdp.WaitAsync(RowsLoaded, deadline.Token);
        await Assert.That((await browser.Cdp.EvaluateAsync(InjectionAbsent, deadline.Token)).GetBoolean()).IsTrue();
        await Assert.That((await browser.Cdp.EvaluateAsync(FocusVisible, deadline.Token)).GetBoolean()).IsTrue();
        await AdminBrowserScopeAssertions.VerifyEmptyScopeAndReconnectAsync(browser.Cdp, fixture, scenario, deadline.Token);
        await AdminBrowserAssertions.DetailsAndNavigationAsync(browser.Cdp, deadline.Token);
        await AdminBrowserViewAssertions.VerifyViewsAsync(browser.Cdp, deadline.Token);
        await AdminBrowserAssertions.CheckViewportAsync(browser.Cdp, AdminBrowserProtocol.DesktopWidth, "desktop", deadline.Token);
        await AdminBrowserAssertions.CheckViewportAsync(browser.Cdp, AdminBrowserProtocol.MobileWidth, "mobile", deadline.Token);
        await AdminBrowserAssertions.DisconnectAsync(browser.Cdp, fixture, deadline.Token);
        await AdminBrowserAssertions.RejectThenReconnectAsync(browser.Cdp, fixture, scenario, deadline.Token);
    }
}
