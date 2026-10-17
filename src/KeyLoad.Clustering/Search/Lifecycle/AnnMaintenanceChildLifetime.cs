namespace KeyLoad.Clustering;

/// <summary>Joins the original ANN child stream before restoring its identity and linked cancellation scopes.</summary>
internal static class AnnMaintenanceChildLifetime
{
    internal static async Task<TResult?> RunAsync<TResult>(IServiceProvider services,
        PrincipalRecord principal, Guid actorId, Guid commandId, NativeRequestWorkChildReservation reservation,
        Func<CancellationToken, Task<TResult>> execute, List<Exception> failures, List<Exception> cleanup,
        CancellationToken token) where TResult : class
    {
        CancellationTokenSource cancellation;
        var created = false;
        try
        {
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, reservation.Cancellation);
            created = true;
            try
            {
                return await RunIdentityAsync(services, principal, actorId, commandId,
                    execute, failures, cleanup, cancellation.Token).ConfigureAwait(true);
            }
            finally
            {
                cancellation.Dispose();
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

    private static async Task<TResult?> RunIdentityAsync<TResult>(IServiceProvider services,
        PrincipalRecord principal, Guid actorId, Guid commandId, Func<CancellationToken, Task<TResult>> execute,
        List<Exception> failures, List<Exception> cleanup, CancellationToken token) where TResult : class
    {
        GrainRequestIdentityScope identity;
        var created = false;
        try
        {
            identity = new GrainRequestIdentityScope(services, principal, actorId, commandId, token,
                connectionId: NativeConnectionExecutionIdentity.Resolve(services));
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
                identity.Dispose();
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
}
