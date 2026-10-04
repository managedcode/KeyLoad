namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteHeavyChildAdmissionTests
{
    [Test]
    public async Task AC_BC_FAIL_014_RealChildrenRespectCapacityAndFifo()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var admission = new SiteHeavyChildAdmission(SiteHeavyChildTokens.ActiveCapacity,
            SiteHeavyChildTokens.FixtureQueueCapacity, TimeSpan.FromMinutes(SiteHeavyChildTokens.AdmissionMinutes));
        await using var first = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var second = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var third = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var fourth = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var fifth = await SiteHeavyChildProcessFixture.CreateAsync(token);
        var one = first.RunAsync(admission, token);
        var two = second.RunAsync(admission, token, github: true);
        await Task.WhenAll(first.WaitStartedAsync(token), second.WaitStartedAsync(token));
        third.ExpectPredecessorExit(first);
        fourth.ExpectPredecessorExit(second);
        fifth.ExpectPredecessorExit(third);
        var three = third.RunAsync(admission, token);
        var four = fourth.RunAsync(admission, token, github: true);
        var five = fifth.RunAsync(admission, token);
        await SiteHeavyChildProcessFixture.WaitQueuedAsync(admission, SiteHeavyChildTokens.FixtureQueueCapacity, token);
        await Assert.That(third.HasStarted || fourth.HasStarted || fifth.HasStarted).IsFalse();
        await first.ReleaseAsync(token);
        await one;
        await third.WaitStartedAsync(token);
        await Assert.That(fourth.HasStarted || fifth.HasStarted).IsFalse();
        await second.ReleaseAsync(token);
        await two;
        await fourth.WaitStartedAsync(token);
        await Assert.That(fifth.HasStarted).IsFalse();
        await third.ReleaseAsync(token);
        await three;
        await fifth.WaitStartedAsync(token);
        await Task.WhenAll(fourth.ReleaseAsync(token), fifth.ReleaseAsync(token));
        foreach (var result in await Task.WhenAll(one, two, three, four, five))
        {
            await Assert.That(result.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
            await Assert.That(result.StandardOutput).IsEqualTo(SiteHeavyChildTokens.FixtureOutput);
            await Assert.That(result.StandardError).IsEqualTo(string.Empty);
        }
        await Assert.That(await third.ReadPredecessorAliveAsync(token)).IsFalse();
        await Assert.That(await fourth.ReadPredecessorAliveAsync(token)).IsFalse();
        await Assert.That(await fifth.ReadPredecessorAliveAsync(token)).IsFalse();
    }

    [Test]
    public async Task AC_BC_FAIL_014_CancelledQueuedChildNeverStartsAndFreesPendingSlot()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var admission = new SiteHeavyChildAdmission(SiteTokens.One, SiteTokens.One,
            TimeSpan.FromMinutes(SiteHeavyChildTokens.AdmissionMinutes));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        await using var active = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var cancelled = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var successor = await SiteHeavyChildProcessFixture.CreateAsync(token);
        var first = active.RunAsync(admission, token);
        await active.WaitStartedAsync(token);
        var rejected = cancelled.RunAsync(admission, cancellation.Token);
        await SiteHeavyChildProcessFixture.WaitQueuedAsync(admission, SiteTokens.One, token);
        await cancellation.CancelAsync();
        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => rejected);
        await Assert.That(cancelled.HasStarted).IsFalse();
        await Assert.That(admission.PendingCount).IsEqualTo(SiteTokens.Zero);
        var next = successor.RunAsync(admission, token);
        await SiteHeavyChildProcessFixture.WaitQueuedAsync(admission, SiteTokens.One, token);
        await active.ReleaseAsync(token);
        await first;
        await successor.WaitStartedAsync(token);
        await successor.ReleaseAsync(token);
        await Assert.That((await next).ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
    }

    [Test]
    public async Task AC_BC_FAIL_014_FullQueueRejectsChildBeforeStart()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var admission = new SiteHeavyChildAdmission(SiteTokens.One, SiteTokens.One,
            TimeSpan.FromMinutes(SiteHeavyChildTokens.AdmissionMinutes));
        await using var active = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var pending = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var rejected = await SiteHeavyChildProcessFixture.CreateAsync(token);
        var first = active.RunAsync(admission, token);
        await active.WaitStartedAsync(token);
        var second = pending.RunAsync(admission, token);
        await SiteHeavyChildProcessFixture.WaitQueuedAsync(admission, SiteTokens.One, token);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => rejected.RunAsync(admission, token));
        await Assert.That(error!.Message).IsEqualTo(SiteHeavyChildTokens.QueueFull);
        await Assert.That(rejected.HasStarted).IsFalse();
        await active.ReleaseAsync(token);
        await first;
        await pending.WaitStartedAsync(token);
        await pending.ReleaseAsync(token);
        await Assert.That((await second).ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
    }

    [Test]
    public async Task AC_BC_FAIL_014_QueueDeadlineRejectsChildBeforeStart()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var admission = new SiteHeavyChildAdmission(SiteTokens.One, SiteTokens.One,
            TimeSpan.FromMilliseconds(SiteHeavyChildTokens.FixtureQueueMilliseconds));
        await using var active = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var rejected = await SiteHeavyChildProcessFixture.CreateAsync(token);
        await using var successor = await SiteHeavyChildProcessFixture.CreateAsync(token);
        var first = active.RunAsync(admission, token);
        await active.WaitStartedAsync(token);
        var expired = rejected.RunAsync(admission, token);
        await SiteHeavyChildProcessFixture.WaitQueuedAsync(admission, SiteTokens.One, token);
        _ = await Assert.ThrowsExactlyAsync<TimeoutException>(() => expired);
        await Assert.That(rejected.HasStarted).IsFalse();
        await Assert.That(token.IsCancellationRequested).IsFalse();
        await Assert.That(admission.PendingCount).IsEqualTo(SiteTokens.Zero);
        var next = successor.RunAsync(admission, token);
        await active.ReleaseAsync(token);
        await first;
        await successor.WaitStartedAsync(token);
        await successor.ReleaseAsync(token);
        await Assert.That((await next).ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
    }
}
