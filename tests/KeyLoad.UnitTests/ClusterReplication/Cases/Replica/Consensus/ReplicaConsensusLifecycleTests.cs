using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-001/002: endpoint shutdown is idempotent while physical stores remain owned by their host.</summary>
internal sealed class ReplicaConsensusLifecycleTests
{
    private const int ConcurrentCallers = 16;
    private const string PrincipalId = "lifecycle-principal";
    private const string EmptyPayload = "{}";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Concurrent DI aliases share one drain and disposal, without disposing the borrowed canonical engine or apply worker.</summary>
    [Test]
    public async Task ConcurrentStopAndDisposeShareDrainAndLeaveBorrowedStoresUsable()
    {
        await using var fixture = new ReplicaLifecycleFixture();
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = Enumerable.Range(0, ConcurrentCallers).Select(index => Task.Run(async () =>
        {
            await start.Task.WaitAsync(linked.Token);
            if (index % 2 == 0)
            {
                await fixture.Consensus.StopAsync(linked.Token);
            }
            else
            {
                await fixture.Consensus.DisposeAsync();
            }
        }, linked.Token)).ToArray();
        start.SetResult();
        await Task.WhenAll(calls).WaitAsync(linked.Token);
        await fixture.Consensus.StopAsync(linked.Token);
        await fixture.Consensus.DisposeAsync();
        await Assert.That(fixture.Consensus.TransportReady.IsCanceled).IsTrue();
        var rejected = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => fixture.Consensus.StateAsync(linked.Token));
        await Assert.That(rejected!.Code).IsEqualTo(ErrorCode.OwnershipLost);
        fixture.Log.SaveTermAndVote(1, fixture.Configuration.LocalId);
        fixture.Log.Append([new(1, 1, null), new(2, 1, null)]);
        fixture.Materializer.Commit(2);
        await fixture.Materializer.WaitForApplyAsync(2, linked.Token);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(2);
        await Assert.That(fixture.Log.State.CommittedIndex).IsEqualTo(2);
    }

    /// <summary>Repeated shutdown rejects endpoint admission with typed ownership errors rather than touching a disposed CTS.</summary>
    [Test]
    public async Task RepeatedStopAndDisposeFenceAllNewEndpointActivity()
    {
        await using var fixture = new ReplicaLifecycleFixture();
        await fixture.Consensus.StopAsync(CancellationToken.None);
        await fixture.Consensus.StopAsync(CancellationToken.None);
        await fixture.Consensus.DisposeAsync();
        await fixture.Consensus.DisposeAsync();
        await fixture.Consensus.StopAsync(CancellationToken.None);
        var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.Batch, PrincipalId,
            TimeProvider.System.GetUtcNow(), EmptyPayload);
        var submit = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => fixture.Consensus.SubmitAsync(operation, CancellationToken.None));
        var barrier = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => fixture.Consensus.ReadBarrierAsync(CancellationToken.None));
        var incoming = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => fixture.Consensus.HandleAsync(ReplicaRpc.RequestVote,
            ReadOnlyMemory<byte>.Empty, CancellationToken.None));
        await Assert.That(submit!.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(barrier!.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(incoming!.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(0);
    }
}
