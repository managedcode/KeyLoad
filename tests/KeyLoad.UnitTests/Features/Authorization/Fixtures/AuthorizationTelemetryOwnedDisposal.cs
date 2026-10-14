using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationTelemetryOwnedDisposal
{
    internal static void Run(IReadOnlyList<Action> originalDisposals)
    {
        var failures = new List<Exception>();
        foreach (var dispose in originalDisposals)
        {
            try
            {
                dispose();
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
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
