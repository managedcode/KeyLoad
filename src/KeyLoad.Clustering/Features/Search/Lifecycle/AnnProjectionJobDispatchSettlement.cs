using System.Collections.Immutable;
using System.Security.Cryptography;

namespace KeyLoad.Clustering;

internal static class AnnProjectionJobDispatchSettlement
{
    private const int NoFailures = 0;

    internal static async Task<AnnMaintenanceResult?> RunOwnedAsync(IServiceProvider services,
        PrincipalRecord principal, Guid requestId, Guid commandId, Guid connectionId,
        NativeRequestWorkChildReservation reservation, Func<CancellationToken, Task<AnnMaintenanceResult>> execute,
        List<Exception> failures, List<Exception> cleanup, CancellationToken token)
    {
        CancellationTokenSource? cancellation = null;
        var created = false;
        try
        {
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, reservation.Cancellation);
            created = true;
            try
            {
                return await RunIdentityAsync(services, principal, requestId, commandId, connectionId,
                    execute, failures, cleanup, cancellation.Token).ConfigureAwait(true);
            }
            finally
            {
                cancellation?.Dispose();
            }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            (created ? cleanup : failures).Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            (created ? cleanup : failures).Add(failure);
        }
        return null;
    }

    private static async Task<AnnMaintenanceResult?> RunIdentityAsync(IServiceProvider services,
        PrincipalRecord principal, Guid requestId, Guid commandId, Guid connectionId,
        Func<CancellationToken, Task<AnnMaintenanceResult>> execute,
        List<Exception> failures, List<Exception> cleanup, CancellationToken token)
    {
        GrainRequestIdentityScope? identity = null;
        var created = false;
        try
        {
            identity = new GrainRequestIdentityScope(services, principal, requestId,
                commandId, token, connectionId: connectionId);
            created = true;
            try
            {
                return await execute(token).ConfigureAwait(true);
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            finally
            {
                identity?.Dispose();
            }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            (created ? cleanup : failures).Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            (created ? cleanup : failures).Add(failure);
        }
        return null;
    }

    internal static async Task CompleteAsync(NativeRequestWorkChildReservation? reservation,
        byte[]? payload, List<Exception> failures, List<Exception> cleanup)
    {
        var original = failures.Count == NoFailures ? null : failures[NoFailures];
        Capture(() =>
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }, cleanup);
        failures.AddRange(cleanup);
        if (reservation is null)
        {
            return;
        }
        try
        {
            _ = await reservation.CompleteDispatchAsync(original, cleanup.ToImmutableArray()).ConfigureAwait(true);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

    private static void Capture(Action action, List<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }
}
