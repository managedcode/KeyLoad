using System.ComponentModel;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteHeavyChildCleanupTests
{
    [Test]
    public async Task AC_BC_FAIL_014_NativeStartFailureReleasesSafeLease()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var admission = NewAdmission();
        await using var active = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var missing = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var successor = await SiteHeavyChildProcessFixture.CreateAsync(token);
        var first = active.RunAsync(admission, token);
        await active.WaitStartedAsync(token);
        var failure = missing.RunAsync(admission, token, missingExecutable: true);
        var next = successor.RunAsync(admission, token);
        await SiteHeavyChildProcessFixture.WaitQueuedAsync(admission, SiteHeavyChildTokens.ActiveCapacity, token);
        await active.ReleaseAsync(token);
        await first;
        _ = await Assert.ThrowsExactlyAsync<Win32Exception>(() => failure);
        await Assert.That(missing.HasStarted).IsFalse();
        await successor.WaitStartedAsync(token);
        await successor.ReleaseAsync(token);
        await Assert.That((await next).ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AC_BC_FAIL_014_CallerCancellationWaitsForNativeExitBeforeSuccessorStarts(bool github)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var admission = NewAdmission();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        await using var active = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var successor = await SiteHeavyChildProcessFixture.CreateAsync(token);
        var first = active.RunAsync(admission, cancellation.Token, github);
        await active.WaitStartedAsync(token);
        successor.ExpectPredecessorExit(active);
        var next = successor.RunAsync(admission, token, github);
        await SiteHeavyChildProcessFixture.WaitQueuedAsync(admission, SiteTokens.One, token);
        await Assert.That(successor.HasStarted).IsFalse();
        await cancellation.CancelAsync();
        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        await active.WaitExitedAsync(token);
        await successor.WaitStartedAsync(token);
        await Assert.That(await successor.ReadPredecessorAliveAsync(token)).IsFalse();
        await successor.ReleaseAsync(token);
        await Assert.That((await next).StandardOutput).IsEqualTo(SiteHeavyChildTokens.FixtureOutput);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AC_BC_FAIL_014_BoundedOutputFailureSettlesOwnedReadersBeforeSuccessor(bool github)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var admission = NewAdmission();
        await using var overflowing = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var successor = await SiteHeavyChildProcessFixture.CreateAsync(token);
        var failure = overflowing.RunAsync(admission, token, github, overflow: true);
        await overflowing.WaitStartedAsync(token);
        successor.ExpectPredecessorExit(overflowing);
        var next = successor.RunAsync(admission, token, github);
        await SiteHeavyChildProcessFixture.WaitQueuedAsync(admission, SiteTokens.One, token);
        await Assert.That(successor.HasStarted).IsFalse();
        await overflowing.ReleaseAsync(token);
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => failure);
        await overflowing.WaitExitedAsync(token);
        await successor.WaitStartedAsync(token);
        await Assert.That(await successor.ReadPredecessorAliveAsync(token)).IsFalse();
        await successor.ReleaseAsync(token);
        await Assert.That((await next).StandardOutput).IsEqualTo(SiteHeavyChildTokens.FixtureOutput);
    }

    [Test]
    public async Task AC_BC_FAIL_014_UnsettledActualChildPoisonsAdmissionAndRejectsWaiters()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var admission = NewAdmission();
        await using var live = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var pending = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var rejected = await SiteHeavyChildProcessFixture.CreateAsync(token);
        using var lease = await admission.AcquireAsync(token);
        live.StartUnsettled(lease);
        await live.WaitStartedAsync(token);
        var waiting = pending.RunAsync(admission, token);
        await SiteHeavyChildProcessFixture.WaitQueuedAsync(admission, SiteTokens.One, token);
        lease.Dispose();
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => waiting);
        await Assert.That(failure!.Message).IsEqualTo(SiteHeavyChildTokens.UnsafeOwnership);
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => rejected.RunAsync(admission, token));
        await Assert.That(pending.HasStarted || rejected.HasStarted).IsFalse();
        await live.ReleaseAsync(token);
        await live.WaitExitedAsync(token);
    }

    private static SiteHeavyChildAdmission NewAdmission() => new(SiteTokens.One,
        SiteHeavyChildTokens.FixtureQueueCapacity, TimeSpan.FromMinutes(SiteHeavyChildTokens.AdmissionMinutes));
}
