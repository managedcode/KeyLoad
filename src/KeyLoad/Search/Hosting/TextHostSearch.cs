using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal static class TextHostSearch
{
    internal static (ITextProjection Projection, TextIncrementalMaintenanceService Explicit,
        TextOnlineMaintenanceService Online) Open(DatabaseEngine database, string directory,
        ServerRuntimeOptions options, TimeProvider clock, Action<TextFaultStage>? observer = null)
    {
        var resources = TextHostResourceRoots.Register(directory, database.Store.Identity.NodeId, options.RuntimeText);
        RuntimeTextProjection? original = null;
        var failures = new List<Exception>();
        try
        {
            try
            {
                original = new(Path.Combine(directory, TextHostResourceRoots.BootstrapDirectory),
                    options.Core.DatabaseLimits, database.Store.Identity.NodeId, options.RuntimeText, resources: resources);
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

    private static (ITextProjection Projection, TextIncrementalMaintenanceService Explicit,
        TextOnlineMaintenanceService Online) OpenMaintenance(DatabaseEngine database, string directory,
        ServerRuntimeOptions options, TimeProvider clock, Action<TextFaultStage>? observer,
        TextResourceOwnership resources, RuntimeTextProjection original)
    {
        var explicitOwner = new TextIncrementalMaintenanceService(database, directory,
            database.Store.Identity.NodeId, options, clock, resources: resources);
        try
        {
            return OpenOnline(database, directory, options, clock, observer, resources, original, explicitOwner);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            {
                explicitOwner.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception cleanup) when (CqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                RetainCleanup(cleanup, failures);
            }
            catch (Exception cleanup) when (!CqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                RetainCleanup(cleanup, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static (ITextProjection Projection, TextIncrementalMaintenanceService Explicit,
        TextOnlineMaintenanceService Online) OpenOnline(DatabaseEngine database, string directory,
        ServerRuntimeOptions options, TimeProvider clock, Action<TextFaultStage>? observer,
        TextResourceOwnership resources, RuntimeTextProjection original,
        TextIncrementalMaintenanceService explicitOwner)
    {
        var online = new TextOnlineMaintenanceService(database, directory,
            database.Store.Identity.NodeId, options, clock, resources, observer);
        try
        {
            var projection = new TextOnlineProjection(
                () => new TextSelectedProjection(original, explicitOwner, explicitOwner),
                explicitOwner, online, database);
            return (projection, explicitOwner, online);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            {
                online.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception cleanup) when (CqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                RetainCleanup(cleanup, failures);
            }
            catch (Exception cleanup) when (!CqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                RetainCleanup(cleanup, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static void RetainCleanup(Exception cleanup, List<Exception> failures)
    {
        if (!failures.Any(originalFailure => ReferenceEquals(originalFailure, cleanup)))
        {
            failures.Add(cleanup);
        }
    }
}
