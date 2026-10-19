using KeyLoad.IntegrationTests.Features.StorageReplication.Processes;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

[NotInParallel]
internal sealed class AdminBrowserOwnedLifetimeTests
{
    private const string Answer = "6*7";
    private const string InvalidMethod = "KeyLoad.invalidBrowserLifecycleCommand";
    private const int ExpectedAnswer = 42;

    [Test]
    public async Task RealChromeProtocolErrorThenHealthyOperationJoinsOriginalProcessAndReadersAcrossRepeatedDispose()
    {
        using var timeout = new CancellationTokenSource(AdminBrowserProtocol.Deadline, TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, timeout.Token);
        var browser = await AdminBrowserProcess.StartAsync(lifetime.Token);
        var profile = browser.ProfilePath;
        var exit = browser.OriginalExit!;
        var output = browser.OriginalOutput!;
        var error = browser.OriginalError!;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(Directory.Exists(profile)).IsTrue();
            await Assert.That((await browser.Cdp.EvaluateAsync(Answer, lifetime.Token)).GetInt32()).IsEqualTo(ExpectedAnswer);
            var rejected = await OwnedProcessFailureObserver.CaptureAsync(browser.Cdp.CommandAsync(InvalidMethod, new
            {
            }, lifetime.Token));
            await Assert.That(rejected is InvalidOperationException { Message: AdminBrowserProtocol.BrowserFailure }).IsTrue();
            await Assert.That((await browser.Cdp.EvaluateAsync(Answer, lifetime.Token)).GetInt32()).IsEqualTo(ExpectedAnswer);
        }, failures);
        browser.PreserveEvidence = failures.Count != 0;
        var first = browser.DisposeAsync().AsTask();
        var second = browser.DisposeAsync().AsTask();
        await ServerFailureObserver.ObserveAsync(() => first, failures);
        await ServerFailureObserver.ObserveAsync(() => second, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(exit.IsCompletedSuccessfully && output.IsCompletedSuccessfully && error.IsCompletedSuccessfully).IsTrue();
        await Assert.That(ReferenceEquals(exit, browser.OriginalExit)
            && ReferenceEquals(output, browser.OriginalOutput) && ReferenceEquals(error, browser.OriginalError)).IsTrue();
        await Assert.That(Directory.Exists(profile)).IsFalse();
    }
}
