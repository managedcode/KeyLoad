using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-ORL-011: due discovery waits on real canonical apply with a joined clock fallback.</summary>
internal sealed class RecurringDueWaitTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>A real canonical commit wins the wait and the helper returns only after joining its fallback.</summary>
    [Test]
    public async Task AppliedWriteWakesDueWaitAndPersistsTheCommittedDocument()
    {
        await using var fixture = new ReplicaAppliedPositionWaitFixture();
        fixture.ConfigureResource();
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        var wait = RecurringDueWait.WaitForChangeOrFallbackAsync(fixture.Consensus, 0,
            TimeSpan.FromSeconds(10), TimeProvider.System, linked.Token);
        fixture.Commit(ReplicaAppliedPositionWaitTests.FirstDocument);

        await wait.WaitAsync(linked.Token);
        await fixture.AssertDocument(ReplicaAppliedPositionWaitTests.FirstDocument);
        await Assert.That(fixture.Consensus.AppliedPosition).IsEqualTo(1L);
    }

    /// <summary>The system clock fallback completes a cycle when no new canonical write arrives.</summary>
    [Test]
    public async Task ClockFallbackCompletesWithoutChangingTheAppliedPosition()
    {
        await using var fixture = new ReplicaAppliedPositionWaitFixture();
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);

        await RecurringDueWait.WaitForChangeOrFallbackAsync(fixture.Consensus, 0,
            TimeSpan.FromMilliseconds(25), TimeProvider.System, linked.Token).WaitAsync(linked.Token);

        await Assert.That(fixture.Consensus.AppliedPosition).IsEqualTo(0L);
    }

    /// <summary>Canceling one due wait joins its signal and timer without disturbing another waiter's signal.</summary>
    [Test]
    public async Task CancellationJoinsBothWaitBranchesAndLeavesSharedApplySignalUsable()
    {
        await using var fixture = new ReplicaAppliedPositionWaitFixture();
        fixture.ConfigureResource();
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        var canceledWait = RecurringDueWait.WaitForChangeOrFallbackAsync(fixture.Consensus, 0,
            TimeSpan.FromSeconds(10), TimeProvider.System, canceled.Token);
        var activeWait = RecurringDueWait.WaitForChangeOrFallbackAsync(fixture.Consensus, 0,
            TimeSpan.FromSeconds(10), TimeProvider.System, linked.Token);
        await canceled.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => canceledWait);
        fixture.Commit(ReplicaAppliedPositionWaitTests.FirstDocument);
        await activeWait.WaitAsync(linked.Token);

        await fixture.AssertDocument(ReplicaAppliedPositionWaitTests.FirstDocument);
        await Assert.That(fixture.Consensus.AppliedPosition).IsEqualTo(1L);
    }
}
