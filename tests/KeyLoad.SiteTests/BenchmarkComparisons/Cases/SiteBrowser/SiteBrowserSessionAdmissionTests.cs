namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteBrowserSessionAdmissionTests
{
    [Test]
    public async Task AC_BC_CHROME_001_RealSessionsSerializeAndQueuedCancellationSettles()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var browserPath = Environment.GetEnvironmentVariable(SiteBrowserTokens.BrowserEnvironment);
        await using var temporary = SiteTempDirectory.Create();
        var admission = SiteBrowserSessionAdmission.Shared;
        var firstProfile = CreateProfile(temporary.Path, "first");
        var cancelledProfile = CreateProfile(temporary.Path, "cancelled");
        var nextProfile = CreateProfile(temporary.Path, "next");
        await using var first = await SiteBrowserChrome.StartAsync(browserPath!, firstProfile, token);
        var firstProcessId = first.Process.Id;
        var firstProcessStartTime = first.Process.StartTime.ToUniversalTime();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pendingBeforeCancellation = admission.PendingCount;
        var cancelled = SiteBrowserChrome.StartAsync(browserPath!, cancelledProfile, cancellation.Token);
        await Assert.That(admission.PendingCount > pendingBeforeCancellation).IsTrue();
        await Assert.That(admission.HasPendingWaiter(cancellation.Token)).IsTrue();
        await cancellation.CancelAsync();
        OperationCanceledException? cancelledError = null;
        try
        {
            await cancelled;
        }
        catch (OperationCanceledException error)
        {
            cancelledError = error;
        }

        await Assert.That(cancelledError is not null).IsTrue();
        await Assert.That(cancelledError!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(admission.HasPendingWaiter(cancellation.Token)).IsFalse();
        await Assert.That(admission.PendingCount >= pendingBeforeCancellation).IsTrue();
        await Assert.That(File.Exists(Path.Combine(cancelledProfile, SiteBrowserTokens.ActivePortFile))).IsFalse();

        var next = SiteBrowserChrome.StartAsync(browserPath!, nextProfile, token);
        await Assert.That(admission.PendingCount > SiteTokens.Zero).IsTrue();
        await Assert.That(next.IsCompleted).IsFalse();
        await Assert.That(first.Process.HasExited).IsFalse();
        _ = await first.CompleteCoverageAsync(token);
        await first.DisposeAsync();
        await Assert.That(first.CoverageStopped).IsTrue();
        await Assert.That(first.CdpDisposed).IsTrue();
        await Assert.That(first.OriginalProcessExitObserved).IsTrue();

        await using var successor = await next;
        var successorIsNewProcess = successor.Process.Id != firstProcessId ||
            successor.Process.StartTime.ToUniversalTime() != firstProcessStartTime;
        await Assert.That(successorIsNewProcess).IsTrue();
        _ = await successor.CompleteCoverageAsync(token);
        await successor.DisposeAsync();
        await Assert.That(successor.OriginalProcessExitObserved).IsTrue();
    }

    private static string CreateProfile(string root, string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
