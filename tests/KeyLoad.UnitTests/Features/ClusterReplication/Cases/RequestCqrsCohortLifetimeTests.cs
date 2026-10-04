using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class RequestCqrsCohortLifetimeTests
{
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ShutdownSharesSettlementStopsAdmissionAndReleasesOnlyAfterLeaseExit()
    {
        var resourcesReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCount = 0;
        using var stopping = new CancellationTokenSource();
        await using var lifetime = new ReplicaDiscoveryLifetime(stopping, () =>
        {
            Interlocked.Increment(ref releaseCount);
            resourcesReleased.TrySetResult();
        });
        using var operation = lifetime.TryEnter();
        await Assert.That(operation).IsNotNull();
        var ownedOperation = operation!;
        var shutdownCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ownedOperation.ShutdownToken.Register(shutdownCancellation.SetResult);
        var firstShutdown = lifetime.StopAsync();
        var concurrentShutdown = lifetime.StopAsync();

        await shutdownCancellation.Task.WaitAsync(TestBound);
        await Assert.That(ReferenceEquals(firstShutdown, concurrentShutdown)).IsTrue();
        await Assert.That(lifetime.TryEnter()).IsNull();
        await Assert.That(firstShutdown.IsCompleted).IsFalse();
        await Assert.That(Volatile.Read(ref releaseCount)).IsEqualTo(0);

        ownedOperation.Dispose();
        await firstShutdown.WaitAsync(TestBound);
        await resourcesReleased.Task.WaitAsync(TestBound);
        await Assert.That(Volatile.Read(ref releaseCount)).IsEqualTo(1);
        await Assert.That(lifetime.IsStopping).IsTrue();
    }
}
