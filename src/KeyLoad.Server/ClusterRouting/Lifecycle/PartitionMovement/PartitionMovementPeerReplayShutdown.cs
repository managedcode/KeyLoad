using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementPeerReplayShutdown
{
    internal static void Dispose(PartitionMovementReplayCache replay, PartitionMovementReplayCache nativeReplay)
    {
        var failures = new List<Exception>();
        try
        {
            nativeReplay.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        try
        {
            replay.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
