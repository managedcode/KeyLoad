using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineCatalogOwner
{
    private readonly NativeTextSelectedReadAdmission onlineReadAdmission = new(options.NativeText.Value.MaximumActiveLeases);
    internal NativeTextSelectedReadAdmission OnlineReadAdmission => onlineReadAdmission;

    internal NativeTextSelectedProjectionLease? AcquireCurrent(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
    {
        var current = database.ReadOnlineTextImplicitCurrent(view, principal, request.Partition,
            request.Collection, request.TextField ?? throw NativeTextErrors.Mismatch(), database.Store.Identity.NodeId, budget);
        if (current is null)
        { return null; }
        var maintenance = new OnlineTextIndexMaintenanceRequest(current.CommandId, current.Consumer,
            current.Authority.Collection, current.Authority.Field, current.ConsumerGeneration,
            current.PublishedCut.NodeId, current.Authority.Placement);
        var generation = RequireGeneration(current, budget);
        var proof = NativeTextOnlineCatalogProof.CaptureFromView(database, view, principal, maintenance,
            generation, budget, clock, options.NativeText);
        NativeTextSelectedProjectionLease? actual = null;
        Exception? primary = null;
        try
        {
            try
            {
                NativeTextOnlineCatalogFiles.RequireCurrentCatalog(root.Directory, proof.Catalog, budget, options.NativeText);
                var scope = TextProjectionLifecycle.CreateScope(database, principal, resource, request);
                if (proof.Catalog.Scope != scope)
                { throw NativeTextErrors.Mismatch(); }
                actual = new NativeTextSelectedProjectionLease(OnlineReadAdmission,
                    () => (generation, generation.Manifest), budget, options.Core.DatabaseLimits);
            }
            catch (Exception error) { primary = error; throw; }
            finally { proof.Dispose(); }
            return actual;
        }
        catch (Exception cleanup)
        {
            var failures = new List<Exception>();
            if (primary is not null && !ReferenceEquals(primary, cleanup))
            { failures.Add(primary); }
            failures.Add(cleanup);
            if (actual is not null)
            { ServerFailureObserver.Observe(actual.Dispose, failures); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
}
