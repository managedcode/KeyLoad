using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.GraphTraversal;

internal sealed class RemoteGraphNativeResources : IDisposable
{
    private const int Active = 0;
    private const int Disposed = 1;
    private readonly CancellationTokenSource stopping;
    private readonly PartitionMovementReplayCache replay;
    private readonly SemaphoreSlim admission;
    private int disposed = Active;

    internal RemoteGraphNativeResources(TimeProvider clock, IOptions<OrleansMembershipOptions> options)
    {
        CancellationTokenSource? acquiredStopping = null;
        PartitionMovementReplayCache? acquiredReplay = null;
        SemaphoreSlim? acquiredAdmission = null;
        try
        {
            acquiredStopping = new();
            acquiredReplay = new(clock, options);
            acquiredAdmission = new(options.Value.MaximumAdmissions, options.Value.MaximumAdmissions);
            stopping = acquiredStopping;
            replay = acquiredReplay;
            admission = acquiredAdmission;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            ReleaseFailedCreation(error);
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            ReleaseFailedCreation(error);
            throw;
        }

        void ReleaseFailedCreation(Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (acquiredAdmission is not null)
            {
                ServerFailureObserver.Observe(acquiredAdmission.Dispose, failures);
            }
            if (acquiredReplay is not null)
            {
                ServerFailureObserver.Observe(acquiredReplay.Dispose, failures);
            }
            if (acquiredStopping is not null)
            {
                ServerFailureObserver.Observe(acquiredStopping.Dispose, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal CancellationTokenSource Stopping => stopping;
    internal SemaphoreSlim Admission => admission;
    internal PartitionMovementReplayCache Replay => replay;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, Disposed) != Active)
        {
            return;
        }
        var failures = new List<Exception>();
        try
        {
            admission.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        {
            replay.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        {
            stopping.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
