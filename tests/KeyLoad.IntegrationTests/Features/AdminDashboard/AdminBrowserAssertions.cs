using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal static class AdminBrowserAssertions
{
    private const string Connected = "document.querySelector('#connection-state')?.textContent.trim().toLowerCase()==='connected'";
    private const string NoOverflow = "document.documentElement.scrollWidth<=innerWidth+1";
    private const string CleanStorage = "localStorage.length===0&&sessionStorage.length===0";
    private const string InjectionAbsent = "window.dashboardInjection!==true";
    private const string ConnectScript = "(()=>{const values=VALUE;for(const [id,value] of Object.entries(values)){document.getElementById(id).value=value;}document.getElementById('connect-form').requestSubmit();return true;})()";
    private const string DisconnectScript = "document.querySelector('#disconnect').click();true";
    private const string Disconnected = "document.querySelector('#connection-state')?.textContent.toLowerCase().includes('disconnected')===true";
    private const string InvalidCredential = "root.invalid-untrusted-credential-long-enough";
    private const string RejectCredentialScript = "(()=>{document.getElementById('api-key').value=VALUE;document.getElementById('connect-form').requestSubmit();return true;})()";
    private const string Unauthorized = "document.getElementById('status-message').textContent.includes('Administrator authorization failed')";

    internal static async Task ConnectAsync(AdminBrowserCdp browser, ClusterFixture fixture,
        AdminDashboardScenario scenario, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["api-key"] = fixture.AdminKey, ["tenant-id"] = scenario.Partition.TenantId,
            ["database-id"] = scenario.Partition.DatabaseId, ["partition-key"] = scenario.Partition.PartitionKey
        };
        var script = ConnectScript.Replace("VALUE", JsonSerializer.Serialize(values), StringComparison.Ordinal);
        await browser.EvaluateAsync(script, cancellationToken);
        await browser.WaitAsync(Connected, cancellationToken);
        await Assert.That((await browser.EvaluateAsync(CleanStorage, cancellationToken)).GetBoolean()).IsTrue();
        var url = (await browser.EvaluateAsync("location.href", cancellationToken)).GetString()!;
        await Assert.That(url.Contains(fixture.AdminKey, StringComparison.Ordinal)).IsFalse();
    }

    internal static async Task CheckViewportAsync(AdminBrowserCdp browser, int width, string name,
        CancellationToken cancellationToken)
    {
        await browser.CommandAsync(AdminBrowserProtocol.SetMetrics,
            new { width, height = AdminBrowserProtocol.Height, deviceScaleFactor = 1, mobile = width == AdminBrowserProtocol.MobileWidth }, cancellationToken);
        await Assert.That((await browser.EvaluateAsync(NoOverflow, cancellationToken)).GetBoolean()).IsTrue();
        Directory.CreateDirectory(AdminBrowserProtocol.EvidenceDirectory);
        var reply = await browser.CommandAsync(AdminBrowserProtocol.CaptureScreenshot, new { format = "png" }, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(AdminBrowserProtocol.EvidenceDirectory, name + ".png"),
            Convert.FromBase64String(reply.GetProperty("data").GetString()!), cancellationToken);
    }

    internal static async Task DisconnectAsync(AdminBrowserCdp browser, ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        await browser.EvaluateAsync(DisconnectScript, cancellationToken);
        await browser.WaitAsync(Disconnected, cancellationToken);
        await Assert.That((await browser.EvaluateAsync(CleanStorage, cancellationToken)).GetBoolean()).IsTrue();
        await Assert.That((await browser.EvaluateAsync(InjectionAbsent, cancellationToken)).GetBoolean()).IsTrue();
        var body = (await browser.EvaluateAsync("document.body.innerText", cancellationToken)).GetString()!;
        await Assert.That(body.Contains(fixture.AdminKey, StringComparison.Ordinal)).IsFalse();
        await Assert.That(body.Contains(AdminDashboardScenario.DocumentId, StringComparison.Ordinal)).IsFalse();
        await Assert.That(body.Contains(AdminDashboardScenario.MessageId, StringComparison.Ordinal)).IsFalse();
        await Assert.That((await browser.EvaluateAsync("document.getElementById('api-key').value.length===0", cancellationToken)).GetBoolean()).IsTrue();
    }

    internal static async Task DetailsAndNavigationAsync(AdminBrowserCdp browser, CancellationToken cancellationToken)
    {
        await browser.EvaluateAsync("document.querySelector('#data-table button').click();true", cancellationToken);
        await browser.WaitAsync("document.querySelector('#details-dialog').open===true", cancellationToken);
        await Assert.That((await browser.EvaluateAsync("document.querySelector('#details-dialog pre').textContent.includes('<script>')", cancellationToken)).GetBoolean()).IsTrue();
        await browser.EvaluateAsync("document.querySelector('#details-dialog').close();true", cancellationToken);
        await browser.EvaluateAsync("document.querySelector('button[data-view=queues]').click();true", cancellationToken);
        await browser.WaitAsync("document.querySelector('#resource-list').textContent.includes('dashboard-jobs')", cancellationToken);
        await browser.EvaluateAsync("document.querySelector('#resource-list button').click();true", cancellationToken);
        await browser.WaitAsync("document.querySelector('#data-table').textContent.includes('message-1')", cancellationToken);
        foreach (var view in new[] { "files", "cluster", "overview" })
        {
            await browser.EvaluateAsync("document.querySelector('button[data-view=" + view + "]').click();true", cancellationToken);
            await Assert.That((await browser.EvaluateAsync("document.querySelector('button[data-view=" + view + "]').getAttribute('aria-current')==='page'", cancellationToken)).GetBoolean()).IsTrue();
        }
    }

    internal static async Task RejectThenReconnectAsync(AdminBrowserCdp browser, ClusterFixture fixture,
        AdminDashboardScenario scenario, CancellationToken cancellationToken)
    {
        await browser.EvaluateAsync(RejectCredentialScript.Replace("VALUE", JsonSerializer.Serialize(InvalidCredential),
            StringComparison.Ordinal), cancellationToken);
        await browser.WaitAsync(Unauthorized, cancellationToken);
        await Assert.That((await browser.EvaluateAsync(Disconnected, cancellationToken)).GetBoolean()).IsTrue();
        await ConnectAsync(browser, fixture, scenario, cancellationToken);
        await DisconnectAsync(browser, fixture, cancellationToken);
    }
}
