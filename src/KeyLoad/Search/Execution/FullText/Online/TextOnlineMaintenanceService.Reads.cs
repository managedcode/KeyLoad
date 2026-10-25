using KeyLoad.Core;
using KeyLoad.Query.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Search;

internal sealed partial class TextOnlineCatalogOwner
{
    private readonly TextSelectedReadAdmission onlineReadAdmission = new();
    internal TextSelectedReadAdmission OnlineReadAdmission => onlineReadAdmission;

    internal global::ManagedCode.Communication.Result<TextSelectedProjectionLease?> AcquireCurrent(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
    {
        var current = (database.ReadOnlineTextImplicitCurrent(view, principal, request.Partition,
            request.Collection, request.TextField ?? throw TextErrors.Mismatch(), database.Store.Identity.NodeId, budget)).Value;
        if (current is null)
        {
            return global::ManagedCode.Communication.Result<TextSelectedProjectionLease?>.FailNotFound(nameof(AcquireCurrent));
        }
        var maintenance = new OnlineTextIndexMaintenanceRequest(current.CommandId, current.Consumer,
            current.Authority.Collection, current.Authority.Field, current.ConsumerGeneration,
            current.PublishedCut.NodeId, current.Authority.Placement);
        var generation = RequireGeneration(current, budget);
        var proof = TextOnlineCatalogProof.CaptureFromView(database, view, principal, maintenance,
            generation, budget, clock, options.RuntimeText);
        TextSelectedProjectionLease? actual = null;
        Exception? primary = null;
        try
        {
            try
            {
                TextOnlineCatalogFiles.RequireCurrentCatalog(root.Directory, proof.Catalog, budget, options.RuntimeText);
                var scope = TextProjectionLifecycle.CreateScope(database, principal, resource, request);
                if (proof.Catalog.Scope != scope)
                {
                    throw TextErrors.Mismatch();
                }
                actual = new TextSelectedProjectionLease(OnlineReadAdmission,
                    () => (generation, generation.Manifest), budget);
            }
            catch (Exception error)
            {
                primary = error;
                throw;
            }
            finally
            {
                proof.Dispose();
            }
            return actual;
        }
        catch (Exception cleanup)
        {
            var failures = new List<Exception>();
            if (primary is not null && !ReferenceEquals(primary, cleanup))
            {
                failures.Add(primary);
            }
            failures.Add(cleanup);
            if (actual is not null)
            {
                ServerFailureObserver.Observe(actual.Dispose, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
}
