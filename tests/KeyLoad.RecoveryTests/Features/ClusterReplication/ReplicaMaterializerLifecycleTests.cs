using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-002/004: terminal materializer disposal drains real durable apply and closes synchronization once.</summary>
internal sealed class ReplicaMaterializerLifecycleTests
{
    private const int ConcurrentCallers = 8;
    private const long EmptyCut = 0;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Disposal callers share one pending terminal task while real canonical apply remains paused after journal flush.</summary>
    [Test]
    public async Task ConcurrentDisposeWaitsForDurableCanonicalApplyBeforeClosingOwners()
    {
        await using var fixture = new ReplicaMaterializerLifecycleFixture();
        await fixture.RunAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
            fixture.PauseCommittedApply();
            await fixture.Entered.WaitAsync(linked.Token);
            Task[] terminal;
            try
            {
                terminal = await ConcurrentDisposalsAsync(fixture.Materializer, linked.Token);
                foreach (var task in terminal)
                {
                    await Assert.That(task).IsSameReferenceAs(terminal[0]);
                    await Assert.That(task.IsCompleted).IsFalse();
                }
            }
            finally { fixture.Release(); }
            await Task.WhenAll(terminal).WaitAsync(linked.Token);
            await Assert.That(fixture.Database.LastApplied).IsEqualTo(ReplicaMaterializerLifecycleFixture.AppliedCut);
            await AssertClosedGateAsync(fixture.Materializer);
            await fixture.AssertReopenedAsync(ReplicaMaterializerLifecycleFixture.AppliedCut);
        });
    }

    /// <summary>Concurrent and repeated normal disposal returns the same successful terminal task and closes the real protocol gate.</summary>
    [Test]
    public async Task ConcurrentAndRepeatedDisposeShareOneSuccessfulTerminalTask()
    {
        await using var fixture = new ReplicaMaterializerLifecycleFixture();
        await fixture.RunAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
            var terminal = await ConcurrentDisposalsAsync(fixture.Materializer, linked.Token);
            await Task.WhenAll(terminal).WaitAsync(linked.Token);
            foreach (var task in terminal)
            {
                await Assert.That(task).IsSameReferenceAs(terminal[0]);
                await Assert.That(task.IsCompletedSuccessfully).IsTrue();
            }
            var repeated = fixture.Materializer.DisposeAsync().AsTask();
            await Assert.That(repeated).IsSameReferenceAs(terminal[0]);
            await repeated.WaitAsync(linked.Token);
            await AssertClosedGateAsync(fixture.Materializer);
            await fixture.AssertReopenedAsync(EmptyCut);
        });
    }

    private static async Task<Task[]> ConcurrentDisposalsAsync(ReplicaMaterializer materializer, CancellationToken cancellationToken)
    {
        var calls = Enumerable.Range(0, ConcurrentCallers).Select(index => Task.Run(
            () => (Index: index, Terminal: materializer.DisposeAsync().AsTask()), cancellationToken)).ToArray();
        var receipts = await Task.WhenAll(calls).WaitAsync(cancellationToken);
        return receipts.Select(receipt => receipt.Terminal).ToArray();
    }

    /// <summary>The real provider's converted journal failure fences an existing waiter and still permits complete materializer cleanup.</summary>
    [Test]
    public async Task ConvertedDurableJournalFailureFencesWaiterAndReopensCommittedPrefix()
    {
        await using var fixture = new ReplicaMaterializerLifecycleFixture();
        await fixture.RunAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
            var waiter = fixture.Materializer.WaitForApplyAsync(ReplicaMaterializerLifecycleFixture.AppliedCut, linked.Token);
            fixture.FailCommittedApply();
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => waiter.WaitAsync(linked.Token));
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.RecoveryRequired);
            await Assert.That(fixture.Entered.IsCompletedSuccessfully).IsTrue();
            var terminal = fixture.Materializer.DisposeAsync().AsTask();
            var concurrent = fixture.Materializer.DisposeAsync().AsTask();
            await Assert.That(concurrent).IsSameReferenceAs(terminal);
            await Task.WhenAll(terminal, concurrent).WaitAsync(linked.Token);
            await Assert.That(terminal.IsCompletedSuccessfully).IsTrue();
            await Assert.That(concurrent.IsCompletedSuccessfully).IsTrue();
            await AssertClosedGateAsync(fixture.Materializer);
            await fixture.AssertReopenedAsync(ReplicaMaterializerLifecycleFixture.AppliedCut);
        });
    }

    private static async Task AssertClosedGateAsync(ReplicaMaterializer materializer)
    {
        var error = Assert.ThrowsExactly<ObjectDisposedException>(() => materializer.ProtocolGate.Wait(0));
        await Assert.That(error).IsNotNull();
    }
}
