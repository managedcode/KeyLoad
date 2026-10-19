using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClientApi;
using Microsoft.Playwright;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AdminDashboardBrowserTests(ClusterFixture fixture)
{
    private const string AdminPath = "/admin/";
    private const string InvalidCredential = "root.invalid-untrusted-credential-long-enough";
    private const string EmptyTenantPrefix = "dashboard-empty-";
    private const string EmptyDatabase = "empty-database";
    private const string GuidFormat = "N";
    private const string Interactive = ".console[data-interactive=true]";
    private const string DataTable = "#data-table";
    private const string Status = "#status-message";
    private const string Tenant = "#tenant-id";
    private const string Database = "#database-id";
    private const string ScopeSubmit = "#scope-form button[type=submit]";
    private const string InjectionAbsent = "window.dashboardInjection !== true";
    private const string CleanStorage = "localStorage.length === 0 && sessionStorage.length === 0";
    private const string Collections = "collections";
    private const string Catalog = "catalog";
    private const string Queues = "queues";
    private const string DocumentText = "<script>";

    [Test]
    public async Task AcAd006PlaywrightBrowsesServerRenderedFluentConsoleAndClearsSession()
    {
        using var timeout = new CancellationTokenSource(AdminBrowserProtocol.Deadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, timeout.Token);
        var scenario = await AdminDashboardScenario.CreateAsync(fixture, deadline.Token);
        using var http = fixture.App.CreateHttpClient(McpCallerProtocol.Node1, McpCallerProtocol.HttpEndpoint);
        await using var browser = await AdminBrowserFixture.StartAsync(deadline.Token);
        var page = browser.Page;
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(new Uri(http.BaseAddress!, AdminPath).AbsoluteUri);
        await page.Locator(Interactive).WaitForAsync();
        await ConnectAsync(page, fixture.AdminKey);
        await SelectAsync(page, Collections);
        await page.Locator(Tenant).FillAsync(scenario.Partition.TenantId);
        await page.Locator(Database).FillAsync(scenario.Partition.DatabaseId);
        await page.Locator("#partition-key").FillAsync(scenario.Partition.PartitionKey);
        await page.Locator(ScopeSubmit).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = AdminDashboardScenario.Collection }).ClickAsync();
        await Assertions.Expect(page.Locator(DataTable)).ToContainTextAsync(AdminDashboardScenario.DocumentId);
        await page.GetByRole(AriaRole.Button, new() { Name = "View JSON" }).ClickAsync();
        await Assertions.Expect(page.Locator("#details-dialog pre")).ToContainTextAsync(DocumentText);
        await page.Locator("#close-details").ClickAsync();
        await Assert.That(await page.EvaluateAsync<bool>(InjectionAbsent)).IsTrue();
        await VerifyEmptyScopeAsync(page, scenario);
        await VerifyViewsAsync(page);
        await AdminPlaywrightAssertions.ViewportsAsync(page);
        await page.Locator("#disconnect").ClickAsync();
        await Assertions.Expect(page.Locator("#connection-state")).ToHaveTextAsync("Disconnected");
        await Assert.That(await page.EvaluateAsync<bool>(CleanStorage)).IsTrue();
        await Assertions.Expect(page.Locator("body")).Not.ToContainTextAsync(AdminDashboardScenario.DocumentId);
        await Assertions.Expect(page.Locator("body")).Not.ToContainTextAsync(fixture.AdminKey);
        await page.Locator("#api-key").FillAsync(InvalidCredential);
        await page.Locator("#connect-button").ClickAsync();
        await Assertions.Expect(page.Locator(Status)).ToContainTextAsync("Administrator authorization failed");
        await ConnectAsync(page, fixture.AdminKey);
        await page.Locator("#disconnect").ClickAsync();
        await Assert.That(errors.Count).IsEqualTo(0);
    }

    private static async Task ConnectAsync(IPage page, string key)
    {
        await page.Locator("#api-key").FillAsync(key);
        await page.Locator("#connect-button").ClickAsync();
        await Assertions.Expect(page.Locator("#connection-state")).ToHaveTextAsync("Connected");
    }

    private static Task SelectAsync(IPage page, string view) => page.Locator("[data-view=" + view + "]").ClickAsync();

    private static async Task VerifyEmptyScopeAsync(IPage page, AdminDashboardScenario scenario)
    {
        await page.Locator(Tenant).FillAsync(EmptyTenantPrefix + Guid.NewGuid().ToString(GuidFormat));
        await page.Locator(Database).FillAsync(EmptyDatabase);
        await page.Locator(ScopeSubmit).ClickAsync();
        await Assertions.Expect(page.Locator("#resource-list")).ToContainTextAsync("No matching resources on this page.");
        await Assertions.Expect(page.Locator(DataTable)).Not.ToContainTextAsync(AdminDashboardScenario.DocumentId);
        await page.Locator(Tenant).FillAsync(scenario.Partition.TenantId);
        await page.Locator(Database).FillAsync(scenario.Partition.DatabaseId);
        await page.Locator(ScopeSubmit).ClickAsync();
        await Assertions.Expect(page.Locator("#resource-list")).ToContainTextAsync(AdminDashboardScenario.Collection);
    }

    private static async Task VerifyViewsAsync(IPage page)
    {
        await SelectAsync(page, Queues);
        await page.GetByRole(AriaRole.Button, new() { Name = AdminDashboardScenario.Queue }).ClickAsync();
        await Assertions.Expect(page.Locator(DataTable)).ToContainTextAsync(AdminDashboardScenario.MessageId);
        await SelectAsync(page, Catalog);
        await Assertions.Expect(page.Locator("#catalog-table")).ToContainTextAsync(AdminDashboardScenario.Collection);
        foreach (var view in new[] { "files", "cluster", "storage", "performance", "errors", "overview" })
        {
            await SelectAsync(page, view);
            await Assertions.Expect(page.Locator("[data-view=" + view + "]")).ToHaveAttributeAsync("aria-current", "page");
        }
        await Assertions.Expect(page.Locator("#topology .node")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator("#topology .self")).ToHaveCountAsync(1);
        await page.Locator("#refresh").FocusAsync();
        await Assertions.Expect(page.Locator("#refresh")).ToBeFocusedAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Assert.That(await page.Locator("#refresh").EvaluateAsync<bool>("element => getComputedStyle(element).transitionDuration === '0s'")).IsTrue();
    }
}
