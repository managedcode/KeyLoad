using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class NativeCanonicalModelCutAdmission(RequestCqrsProbeFiles files,
    RequestCqrsProbeLifecycle lifecycle, IHostApplicationLifetime applicationLifetime,
    IOptions<RequestProbeExecutionOptions> executionOptions, TimeProvider clock, CancellationToken stopping)
{
    internal NativeCanonicalModelCutLease Open(CancellationToken requestCancellation)
    {
        lifecycle.EnterCallback();
        try
        {
            lifecycle.EnterGate();
        }
        catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            ReleaseCallback(error);
            throw;
        }
        catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            ReleaseCallback(error);
            throw;
        }
        try
        {
            return new NativeCanonicalModelCutLease(files, lifecycle,
                executionOptions.Value.HoldTimeout, clock, requestCancellation,
                applicationLifetime.ApplicationStopping, stopping);
        }
        catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            ReleaseAdmission(error);
            throw;
        }
        catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            ReleaseAdmission(error);
            throw;
        }
    }

    private void ReleaseCallback(Exception primary)
    {
        var failures = new List<Exception> { primary };
        ServerFailureObserver.Observe(lifecycle.ExitCallback, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void ReleaseAdmission(Exception primary)
    {
        var failures = new List<Exception> { primary };
        ServerFailureObserver.Observe(lifecycle.ExitGate, failures);
        ServerFailureObserver.Observe(lifecycle.ExitCallback, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
