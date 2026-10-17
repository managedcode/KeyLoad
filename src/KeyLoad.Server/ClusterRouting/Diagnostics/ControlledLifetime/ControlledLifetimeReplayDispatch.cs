using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ControlledLifetimeReplayDispatch
{
    internal static async Task ObserveAsync(PartitionMovementTransportContext context, SiloAddress address,
        string receiver, PartitionMovementTransportRequest original, byte[] body,
        IControlledLifetimeReplayObserver observer, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            await using var owner = new ControlledLifetimeReplayExchange(context, address, receiver, original, body, token);
            await ServerFailureObserver.ObserveAsync(() => observer.ObserveAsync(original, owner, token).AsTask(), failures).ConfigureAwait(false);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
