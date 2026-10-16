using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleWitnessHistory
{
    internal static DropLifecycleRf3ReceiptProof? Read(ZoneTreeStore canonical, ZoneTreeStore replica,
        string replicaDirectory, PhysicalShardRecord owner, string voter, DropTableLifecycleTable table, Guid parentId,
        ZoneTreeConservationBudget work, CancellationToken token)
    {
        var position = canonical.Position;
        var journalPosition = replica.Position;
        var result = canonical.Read(source =>
        {
            var view = new ZoneTreeConservationView(source, work);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(view) ?? throw Invalid();
            if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, owner))
            {
                throw Invalid();
            }
            var limits = IntegrationExecutionOptions.DatabaseLimits().Value;
            var history = ResourceRetirementFinalizationStorage.ReadHistory(view, table.Lifetime, limits);
            if (history is null)
            {
                return null;
            }
            ResourceRetirementFinalizationReceiptValidation.Require(history, limits);
            if (!PhysicalOwnerEntryValidation.SameOwner(history.Control.Catalog.DefaultShard, owner))
            {
                throw Invalid();
            }
            RequireParent(view, table, parentId, history, limits);
            var database = DropTableLifecycleWitnessStores.Database(canonical, owner);
            return DropTableLifecycleWitnessJournal.Read(replica, replicaDirectory, database, view, owner, voter,
                catalog, history, work, token);
        });
        if (canonical.Position != position || replica.Position != journalPosition)
        {
            throw Invalid();
        }
        return result;
    }

    private static void RequireParent(KeyLoad.Storage.IKeyValueView view, DropTableLifecycleTable table,
        Guid parentId, ResourceRetirementFinalizationV1 history, DatabaseLimits limits)
    {
        var lookup = new ResourceRetirementDropParentLookupV1(ResourceRetirementProtocol.Version,
            parentId, table.Request);
        var parent = ResourceRetirementDropParentStorage.Read(view, lookup,
            table.Inventory.Control.PrincipalId, limits) ?? throw Invalid();
        if (!ResourceRetirementValidation.Same(parent.Lifetime, table.Lifetime)
            || !ResourceRetirementValidation.Same(parent.Definition, table.Definition)
            || !ResourceRetirementValidation.Same(parent.RetirementRequest, history.Control.Request)
            || !ResourceRetirementValidation.Same(history.Control.Lifetime, table.Lifetime)
            || history.Control.PrincipalId != parent.PrincipalId)
        {
            throw Invalid();
        }
    }

    internal static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Corruption,
        "The stopped DROP finalization witness does not match its original native owner, parent or committed entry.");
}
