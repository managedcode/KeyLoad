using Microsoft.Playwright;

namespace KeyLoad.ClusterTests.AdminDashboard;

[NotInParallel]
internal sealed class AdminBrowserLifetimeTests
{
    private const string Answer = "6 * 7";
    private const string Rejected = "() => { throw new Error('Intentional browser failure'); }";
    private const int ExpectedAnswer = 42;

    [Test]
    public async Task PlaywrightFailureThenHealthyOperationClosesBrowserAcrossRepeatedDispose()
    {
        var browser = await AdminBrowserFixture.StartAsync(TestContext.Current!.Execution.CancellationToken);
        try
        {
            await Assert.That(await browser.Page.EvaluateAsync<int>(Answer)).IsEqualTo(ExpectedAnswer);
            await Assert.ThrowsAsync<PlaywrightException>(() => browser.Page.EvaluateAsync<int>(Rejected));
            await Assert.That(await browser.Page.EvaluateAsync<int>(Answer)).IsEqualTo(ExpectedAnswer);
        }
        finally
        {
            var first = browser.DisposeAsync().AsTask();
            var second = browser.DisposeAsync().AsTask();
            await Task.WhenAll(first, second);
            await Assert.That(ReferenceEquals(first, second)).IsTrue();
        }
        await Assert.That(browser.Connected).IsFalse();
    }

    [Test]
    public async Task CancellationClosesPlaywrightContextAndBrowserBeforeJoinedDisposeReturns()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var browser = await AdminBrowserFixture.StartAsync(lifetime.Token);
        await lifetime.CancelAsync();
        await browser.DisposeAsync();
        await Assert.That(browser.Page.IsClosed).IsTrue();
        await Assert.That(browser.Connected).IsFalse();
    }

}
