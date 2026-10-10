using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextHostSearch
{
    internal static (ITextProjection Projection, NativeTextIncrementalMaintenanceService Explicit,
        NativeTextOnlineMaintenanceService Online) Open(DatabaseEngine database, string directory,
        ServerRuntimeOptions options, TimeProvider clock, Action<NativeTextFaultStage>? observer = null)
    {
        var resources = NativeTextHostResourceRoots.Register(directory, database.Store.Identity.NodeId, options.NativeText);
        NativeTextProjection? original = null;
        var failures = new List<Exception>();
        try
        {
            try
            {
                original = new(Path.Combine(directory, NativeTextHostResourceRoots.BootstrapDirectory),
                    options.Core.DatabaseLimits, database.Store.Identity.NodeId, options.NativeText, resources: resources);
                var result = OpenMaintenance(database, directory, options, clock, observer, resources, original);
                original = null;
                return result;
            }
            catch (Exception primary) { failures.Add(primary); throw; }
            finally { original?.Dispose(); }
        }
        catch (Exception cleanup)
        {
            RetainCleanup(cleanup, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static (ITextProjection Projection, NativeTextIncrementalMaintenanceService Explicit,
        NativeTextOnlineMaintenanceService Online) OpenMaintenance(DatabaseEngine database, string directory,
        ServerRuntimeOptions options, TimeProvider clock, Action<NativeTextFaultStage>? observer,
        NativeTextResourceOwnership resources, NativeTextProjection original)
    {
        var explicitOwner = new NativeTextIncrementalMaintenanceService(database, directory,
            database.Store.Identity.NodeId, options, clock, resources: resources);
        try
        {
            return OpenOnline(database, directory, options, clock, observer, resources, original, explicitOwner);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { explicitOwner.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { RetainCleanup(cleanup, failures); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { RetainCleanup(cleanup, failures); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static (ITextProjection Projection, NativeTextIncrementalMaintenanceService Explicit,
        NativeTextOnlineMaintenanceService Online) OpenOnline(DatabaseEngine database, string directory,
        ServerRuntimeOptions options, TimeProvider clock, Action<NativeTextFaultStage>? observer,
        NativeTextResourceOwnership resources, NativeTextProjection original,
        NativeTextIncrementalMaintenanceService explicitOwner)
    {
        var online = new NativeTextOnlineMaintenanceService(database, directory,
            database.Store.Identity.NodeId, options, clock, resources, observer);
        try
        {
            var projection = new NativeTextOnlineProjection(
                () => new NativeTextSelectedProjection(original, explicitOwner, explicitOwner),
                explicitOwner, online, database);
            return (projection, explicitOwner, online);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { online.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { RetainCleanup(cleanup, failures); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { RetainCleanup(cleanup, failures); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static void RetainCleanup(Exception cleanup, List<Exception> failures)
    {
        if (!failures.Any(originalFailure => ReferenceEquals(originalFailure, cleanup)))
        { failures.Add(cleanup); }
    }
}
