using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-002/004/051: terminal materializer disposal drains durable apply while preserving borrowed protocol ownership.</summary>
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
            await AssertBorrowedGateAsync(fixture.Materializer, fixture.Log);
            await fixture.AssertReopenedAsync(ReplicaMaterializerLifecycleFixture.AppliedCut);
            await AssertOwnerClosedGateAsync(fixture.Log);
        });
    }

    /// <summary>Concurrent and repeated normal disposal shares one terminal task without closing the log owner's protocol gate.</summary>
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
            await AssertBorrowedGateAsync(fixture.Materializer, fixture.Log);
            await fixture.AssertReopenedAsync(EmptyCut);
            await AssertOwnerClosedGateAsync(fixture.Log);
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
            await AssertBorrowedGateAsync(fixture.Materializer, fixture.Log);
            await fixture.AssertReopenedAsync(ReplicaMaterializerLifecycleFixture.AppliedCut);
            await AssertOwnerClosedGateAsync(fixture.Log);
        });
    }

    private static async Task AssertBorrowedGateAsync(ReplicaMaterializer materializer, DurableReplicaLog log)
    {
        await Assert.That(materializer.ProtocolGate).IsSameReferenceAs(log.ProtocolGate);
        var entered = log.ProtocolGate.Wait(0);
        try
        { await Assert.That(entered).IsTrue(); }
        finally { if (entered) { log.ProtocolGate.Release(); } }
    }

    private static async Task AssertOwnerClosedGateAsync(DurableReplicaLog log)
    {
        var error = Assert.ThrowsExactly<ObjectDisposedException>(() => log.ProtocolGate.Wait(0));
        await Assert.That(error).IsNotNull();
    }
}
