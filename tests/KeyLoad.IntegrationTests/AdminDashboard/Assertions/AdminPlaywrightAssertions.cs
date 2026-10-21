using KeyLoad.IntegrationTests.Features.StorageReplication;
using Microsoft.Playwright;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal static class AdminPlaywrightAssertions
{
    private const string NoOverflow = "document.documentElement.scrollWidth <= innerWidth + 1";
    private const string Desktop = "desktop.png";
    private const string Mobile = "mobile.png";
    private const string Menu = "#menu-toggle";
    private const string Collections = "[data-view=collections]";
    private const string CurrentAttribute = "aria-current";
    private const string CurrentPage = "page";

    internal static async Task InteractiveAsync(IPage page, string address)
    {
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        EventHandler<IConsoleMessage> capture = (_, message) =>
        {
            if (message.Type == "error" && errors.Count < 20)
            {
                errors.Enqueue(message.Text[..Math.Min(message.Text.Length, 512)]);
            }
        };
        page.Console += capture;
        try
        {
            await page.GotoAsync(address);
            await page.Locator(".console[data-interactive=true]").WaitForAsync();
        }
        catch (Exception failure)
        {
            var body = await page.Locator("body").InnerTextAsync();
            throw new InvalidOperationException("Console startup failed: " + page.Url + " | "
                + body[..Math.Min(body.Length, 512)] + " | " + string.Join(Environment.NewLine, errors), failure);
        }
        finally
        {
            page.Console -= capture;
        }
    }

    internal static async Task ViewportsAsync(IPage page)
    {
        var directory = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, AdminBrowserProtocol.EvidenceDirectory);
        Directory.CreateDirectory(directory);
        await page.SetViewportSizeAsync(AdminBrowserProtocol.DesktopWidth, AdminBrowserProtocol.Height);
        await Assert.That(await page.EvaluateAsync<bool>(NoOverflow)).IsTrue();
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, Desktop), FullPage = true });
        await page.SetViewportSizeAsync(AdminBrowserProtocol.MobileWidth, AdminBrowserProtocol.Height);
        await Assert.That(await page.EvaluateAsync<bool>(NoOverflow)).IsTrue();
        await page.Locator(Menu).ClickAsync();
        await page.Locator(Collections).ClickAsync();
        await Assertions.Expect(page.Locator(Collections)).ToHaveAttributeAsync(CurrentAttribute, CurrentPage);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, Mobile), FullPage = true });
    }
}
