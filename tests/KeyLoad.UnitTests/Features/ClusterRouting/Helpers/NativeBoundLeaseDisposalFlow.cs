using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterReplication;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseDisposalFlow
{
    internal static async Task ExecuteAsync(bool failures, bool completionFirst, CancellationToken token)
    {
        var scenario = new P();
        var fixture = new RequestCqrsCohortRuntimeFixture(barrierDeadline: scenario);
        var session = new NativeLocalChildLifetimeSession();
        var errors = new List<Exception>();
        Exception? original = failures ? new InvalidOperationException(nameof(NativeBoundLeaseDisposalFlow)) : null;
        Exception? cleanup = failures ? new IOException(nameof(NativeBoundLeaseDisposalFlow)) : null;
        using var bound = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.StartupBound, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, bound.Token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await fixture.InitializeAsync();
            var state = await NativeLocalChildLifetimeSeed.CreateAsync(fixture, linked.Token);
            var operation = session.Initialize(state, linked.Token);
            await NativeBoundLeaseDisposalOperation.ExecuteAsync(session, state, operation, original, cleanup, completionFirst);
            await NativeBoundLeaseDisposalFollowing.ExecuteAsync(state, operation, linked.Token);
            await NativeLocalChildLifetimeConservation.VerifyAsync(fixture, state, linked.Token);
            var shutdown = operation.Owner.DisposeAsync().AsTask();
            await Assert.That(operation.Owner.DisposeAsync().AsTask()).IsSameReferenceAs(shutdown);
            await shutdown.WaitAsync(RequestCqrsCohortRuntimeFixture.ShutdownBound, linked.Token);
            await Assert.That(operation.Owner.IsJoined).IsTrue();
        }, errors);
        scenario.Release();
        await NativeBoundLeaseDisposalCleanup.JoinAsync(session, cleanup, errors);
        await ServerFailureObserver.ObserveAsync(() => fixture.DisposeAsync().AsTask(), errors);
        ServerFailureObserver.ThrowIfAny(errors);
    }
}
