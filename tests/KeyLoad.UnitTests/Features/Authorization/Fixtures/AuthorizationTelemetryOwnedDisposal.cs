using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationTelemetryOwnedDisposal
{
    internal static void Run(IDisposable? client, IDisposable? app,
        IDisposable completedRequests, IDisposable database)
    {
        var failures = new List<Exception>();
        Observe(client, failures);
        Observe(app, failures);
        Observe(completedRequests, failures);
        Observe(database, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void Observe(IDisposable? resource, List<Exception> failures)
    {
        try
        {
            resource?.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }
}
