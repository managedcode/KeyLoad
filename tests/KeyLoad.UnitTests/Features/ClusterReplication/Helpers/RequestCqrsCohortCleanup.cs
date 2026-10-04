using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class RequestCqrsCohortCleanup
{
    internal static Task CaptureAsync(Func<Task> cleanup, List<Exception> failures)
        => ServerFailureObserver.ObserveAsync(cleanup, failures);

    internal static void CaptureSync(Action cleanup, List<Exception> failures)
        => ServerFailureObserver.Observe(cleanup, failures);

    internal static void ThrowIfAny(IReadOnlyList<Exception> failures)
        => ServerFailureObserver.ThrowIfAny(failures);
}
