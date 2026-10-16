using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterReplication;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseDisposalFlow
{
    internal static async Task ExecuteAsync(bool failures, bool completionFirst, CancellationToken token)
    {
        var scenario = new P();
        var session = new NativeLocalChildLifetimeSession();
        var errors = new List<Exception>();
        Exception? original = failures ? new InvalidOperationException(nameof(NativeBoundLeaseDisposalFlow)) : null;
        Exception? cleanup = failures ? new IOException(nameof(NativeBoundLeaseDisposalFlow)) : null;
        CancellationTokenSource? bound = null;
        CancellationTokenSource? linked = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new RequestCqrsCohortRuntimeFixture(barrierDeadline: scenario);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                bound = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.StartupBound, TimeProvider.System);
                linked = CancellationTokenSource.CreateLinkedTokenSource(token, bound.Token);
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
            await ServerFailureObserver.ObserveAsync(() =>
                NativeBoundLeaseDisposalCleanup.JoinAsync(session, cleanup, errors), errors);
        }, errors);
        ServerFailureObserver.Observe(() => linked?.Dispose(), errors);
        ServerFailureObserver.Observe(() => bound?.Dispose(), errors);
        ServerFailureObserver.ThrowIfAny(errors);
    }
}
