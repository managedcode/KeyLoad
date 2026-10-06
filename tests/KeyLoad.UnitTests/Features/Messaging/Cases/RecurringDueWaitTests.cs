using KeyLoad.Orleans;
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

        var cancellation = await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => canceledWait);
        await Assert.That(canceled.IsCancellationRequested).IsTrue();
        await Assert.That(cancellation!.CancellationToken.IsCancellationRequested).IsTrue();
        fixture.Commit(ReplicaAppliedPositionWaitTests.FirstDocument);
        await activeWait.WaitAsync(linked.Token);

        await fixture.AssertDocument(ReplicaAppliedPositionWaitTests.FirstDocument);
        await Assert.That(fixture.Consensus.AppliedPosition).IsEqualTo(1L);
    }

    /// <summary>A malformed committed replica entry faults the real apply worker and the due wait joins its fallback.</summary>
    [Test]
    public async Task ApplyLogFailureWakesDueWaitAndPreservesRecoveryFence()
    {
        ReplicaAppliedPositionWaitFixture? fixture = null;
        try
        {
            fixture = new ReplicaAppliedPositionWaitFixture();
            using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            var pending = RecurringDueWait.WaitForChangeOrFallbackAsync(fixture.Consensus, 0,
                TimeSpan.FromSeconds(10), TimeProvider.System, linked.Token);

            fixture.CommitCorruptedNoOpEntry();

            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => pending.WaitAsync(linked.Token));
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        }
        finally
        {
            KeyLoadException? disposalFailure = null;
            try
            {
                await (fixture?.DisposeAsync() ?? ValueTask.CompletedTask);
            }
            catch (KeyLoadException error) when (error.GetType() == typeof(KeyLoadException))
            {
                disposalFailure = error;
            }
            if (fixture is not null)
            {
                await Assert.That(disposalFailure).IsNotNull();
                await Assert.That(disposalFailure!.Code).IsEqualTo(ErrorCode.FormatUnsupported);
            }
        }
    }
}
