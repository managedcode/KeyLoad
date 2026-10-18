using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseDisposalFlow
{
    internal static async Task ExecuteAsync(bool failures, bool completionFirst, CancellationToken token)
    {
        var session = new NativeLocalChildLifetimeSession();
        var errors = new List<Exception>();
        Exception? original = failures ? new InvalidOperationException(nameof(NativeBoundLeaseDisposalFlow)) : null;
        Exception? cleanup = failures ? new IOException(nameof(NativeBoundLeaseDisposalFlow)) : null;
        CancellationTokenSource? bound = null;
        CancellationTokenSource? linked = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new RequestCqrsHostedRuntimeFixture();
            await using var other = new RequestCqrsHostedRuntimeFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                bound = new CancellationTokenSource(new NativeRuntimeTestOptions().StartupTimeout, TimeProvider.System);
                linked = CancellationTokenSource.CreateLinkedTokenSource(token, bound.Token);
                await fixture.InitializeAsync(linked.Token);
                await other.InitializeAsync(linked.Token);
                var state = await NativeLocalChildLifetimeSeed.CreateAsync(fixture, other, linked.Token);
                var operation = session.Initialize(state, linked.Token);
                await NativeBoundLeaseDisposalOperation.ExecuteAsync(session, state, operation, original, cleanup, completionFirst);
                await NativeBoundLeaseDisposalFollowing.ExecuteAsync(state, operation, linked.Token);
                await NativeLocalChildLifetimeConservation.VerifyAsync(fixture, state, linked.Token);
                var shutdown = operation.Owner.DisposeAsync().AsTask();
                await Assert.That(operation.Owner.DisposeAsync().AsTask()).IsSameReferenceAs(shutdown);
                await shutdown.WaitAsync(new NativeRuntimeTestOptions().ShutdownTimeout, linked.Token);
                await Assert.That(operation.Owner.IsJoined).IsTrue();
            }, errors);
            await ServerFailureObserver.ObserveAsync(() =>
                NativeBoundLeaseDisposalCleanup.JoinAsync(session, cleanup, errors), errors);
        }, errors);
        ServerFailureObserver.Observe(() => linked?.Dispose(), errors);
        ServerFailureObserver.Observe(() => bound?.Dispose(), errors);
        ServerFailureObserver.ThrowIfAny(errors);
    }
}
