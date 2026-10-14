using KeyLoad.Clustering;
namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementResourceShutdown
{
    internal static async Task JoinAsync(Func<PartitionMovementSourceOwner?> borrowSource,
        Func<PartitionMovementClient?> borrowClient, Func<PartitionMovementPeerAdmission?> borrowAdmission,
        List<Exception> failures)
    {
        if (borrowSource() is not null)
        {
            Task? sourceShutdown = null;
            try
            {
                sourceShutdown = borrowSource()!.DisposeAsync().AsTask();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            if (sourceShutdown is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => sourceShutdown, failures).ConfigureAwait(false);
            }
        }
        if (borrowClient() is not null)
        {
            try
            {
                borrowClient()!.Dispose();
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
        if (borrowAdmission() is not null)
        {
            try
            {
                borrowAdmission()!.Dispose();
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
}
