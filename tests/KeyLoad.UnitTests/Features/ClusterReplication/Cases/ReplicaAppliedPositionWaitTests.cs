namespace KeyLoad.UnitTests;

/// <summary>AC-ORL-011: real committed canonical apply wakes coalesced position waiters safely.</summary>
internal sealed class ReplicaAppliedPositionWaitTests
{
    internal const string Resource = "applied-position-documents";
    internal const string FirstDocument = "first";
    private const string SecondDocument = "second";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>A committed native operation racing waiter registration remains observable in canonical state.</summary>
    [Test]
    public async Task ApplyRacingWaitRegistrationReturnsCommittedPositionAndDocument()
    {
        await using var fixture = new ReplicaAppliedPositionWaitFixture();
        fixture.ConfigureResource();
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = Task.Run(async () =>
        {
            await start.Task.WaitAsync(linked.Token);
            return await fixture.Materializer.WaitForAppliedPositionChangeAsync(0, linked.Token);
        }, linked.Token);
        var commit = Task.Run(async () =>
        {
            await start.Task.WaitAsync(linked.Token);
            fixture.Commit(FirstDocument);
        }, linked.Token);
        start.SetResult();
        await Task.WhenAll(wait, commit).WaitAsync(linked.Token);

        await Assert.That(await wait).IsEqualTo(1L);
        await fixture.AssertDocument(FirstDocument);
        await Assert.That(fixture.Materializer.AppliedPosition).IsEqualTo(1L);
    }

    /// <summary>Several committed writes may share one signal while the waiter returns the final applied cut.</summary>
    [Test]
    public async Task CoalescedCommittedWritesReturnFinalPositionAndPersistEveryDocument()
    {
        await using var fixture = new ReplicaAppliedPositionWaitFixture();
        fixture.ConfigureResource();
        fixture.Commit(FirstDocument, SecondDocument);

        var position = await fixture.Materializer.WaitForAppliedPositionChangeAsync(0, CancellationToken.None)
            .WaitAsync(Timeout, TimeProvider.System);

        await Assert.That(position).IsEqualTo(2L);
        await Assert.That(fixture.Materializer.AppliedPosition).IsEqualTo(2L);
        await fixture.AssertDocument(FirstDocument);
        await fixture.AssertDocument(SecondDocument);
    }

    /// <summary>Canceling one wait leaves the shared applied-position signal available to another caller.</summary>
    [Test]
    public async Task CallerCancellationDoesNotCancelAnotherAppliedPositionWait()
    {
        await using var fixture = new ReplicaAppliedPositionWaitFixture();
        fixture.ConfigureResource();
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        var canceledWait = fixture.Materializer.WaitForAppliedPositionChangeAsync(0, canceled.Token);
        var activeWait = fixture.Materializer.WaitForAppliedPositionChangeAsync(0, linked.Token);
        await canceled.CancelAsync();

        var cancellation = await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => canceledWait);
        await Assert.That(cancellation!.CancellationToken).IsEqualTo(canceled.Token);
        fixture.Commit(FirstDocument);

        await Assert.That(await activeWait.WaitAsync(linked.Token)).IsEqualTo(1L);
        await fixture.AssertDocument(FirstDocument);
    }

    /// <summary>Materializer shutdown wakes and joins a waiter with the typed recovery fence.</summary>
    [Test]
    public async Task ShutdownWakesPendingAppliedPositionWaiter()
    {
        await using var fixture = new ReplicaAppliedPositionWaitFixture();
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        var pending = fixture.Materializer.WaitForAppliedPositionChangeAsync(
            fixture.Materializer.AppliedPosition, linked.Token);

        await fixture.Materializer.DisposeAsync();
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => pending.WaitAsync(linked.Token));

        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(fixture.Canonical.Store.Position > 0).IsTrue();
    }
}
