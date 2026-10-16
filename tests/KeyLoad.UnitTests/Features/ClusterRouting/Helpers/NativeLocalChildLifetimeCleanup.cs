using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterReplication;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeCleanup
{
    internal static async Task JoinAsync(NativeLocalChildLifetimeSession session, List<Exception> failures)
    {
        session.ReleaseProducer.TrySetResult();
        foreach (var producer in session.Producers)
        {
            await ServerFailureObserver.ObserveAsync(() =>
                producer.WaitAsync(RequestCqrsCohortRuntimeFixture.ShutdownBound), failures);
        }
        var original = failures.FirstOrDefault();
        foreach (var reservation in session.Reservations)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var outcome = await reservation.Child.CompleteAsync(original, null);
                if (outcome.CleanupOrJoinFailure is { } cleanup)
                {
                    throw cleanup;
                }
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
                _ = await reservation.CompleteDispatchAsync(original, []), failures);
        }
        if (session.Parent is { } parent)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await parent.PrepareCompletionAsync();
                var outcome = await parent.CompleteAsync(original, null);
                if (outcome.CleanupOrJoinFailure is { } cleanup)
                {
                    throw cleanup;
                }
            }, failures);
        }
        if (session.Owner is { } owner)
        {
            await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures);
        }
        if (session.Ledger is { } ledger && session.Owner is { IsJoined: true })
        {
            ServerFailureObserver.Observe(ledger.Dispose, failures);
        }
        if (session.Linked is { } linked)
        {
            ServerFailureObserver.Observe(linked.Dispose, failures);
        }
    }
}
