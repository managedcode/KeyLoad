using System.Security.Cryptography;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal static class SearchTerminalPrivacy
{
    private const string Changed = "The original search authority is no longer current.";
    private const string Missing = "Current search privacy metadata is unavailable.";

    internal static SearchReadAuthority Capture(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, PartitionRef partition, ReadExecutionBudget budget)
    {
        var identity = database.Store.Identity;
        var applied = budget.Read(view, KeySpace.AppliedBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Missing);
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.ReadGeneration,
            database.Store.Position, OrleansSerialization.Deserialize<long>(applied), principal.PolicyEpoch,
            resource.SchemaVersion, ResourceHash(resource, budget), database.ReadPlacementWitness(view, partition));
    }

    internal static void Verify(DatabaseEngine database, string principalId, PartitionRef partition,
        string collection, string textField, string? vectorField, SearchReadAuthority original,
        IKeyValueView captured, IReadOnlyList<EntityRef> selected, ReadExecutionBudget budget)
    {
        budget.Check();
        database.WithQueryView(principalId, partition, collection,
            vectorField is null ? Capability.None : Capability.VectorSearch, (raw, principal, resource) =>
        {
            RequireAuthority(database, raw, principal, resource, partition, original, budget);
            database.Authorization.RequireFieldUse(principal, resource, textField);
            if (vectorField is not null)
            {
                database.Authorization.Require(principal, partition, collection, Capability.VectorSearch);
                database.Authorization.RequireFieldUse(principal, resource, vectorField);
            }
            foreach (var reference in selected)
            {
                RequireCurrentRow(database, raw, principal, reference, budget);
                if (vectorField is not null)
                {
                    RequireSourcePrivacy(database, raw, captured, principal, reference, vectorField, budget);
                }
            }
            return true;
        }, budget);
        budget.Check();
    }

    private static void RequireAuthority(DatabaseEngine database, IKeyValueView raw, PrincipalRecord principal,
        ResourceDefinition resource, PartitionRef partition, SearchReadAuthority original, ReadExecutionBudget budget)
    {
        var identity = database.Store.Identity;
        var placement = database.ReadPlacementWitness(raw, partition);
        if (identity.NodeId != original.NodeId || identity.Incarnation != original.Incarnation
            || identity.FormatVersion != original.DataEpoch || identity.ReadGeneration != original.ReadGeneration
            || principal.PolicyEpoch != original.PolicyEpoch || resource.SchemaVersion != original.SchemaVersion
            || ResourceHash(resource, budget) != original.ResourceSha256
            || placement.PhysicalShardId != original.Placement.PhysicalShardId
            || placement.Incarnation != original.Placement.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Changed);
        }
        budget.Check();
    }

    private static void RequireSourcePrivacy(DatabaseEngine database, IKeyValueView current, IKeyValueView captured,
        PrincipalRecord principal, EntityRef target, string field, ReadExecutionBudget budget)
    {
        var vector = budget.ReadRecord<VectorRecord>(captured,
            KeySpace.Partition(VisibleVectorReads.VectorKeySpace, target.Partition, target.Collection, field, target.Id));
        var document = budget.ReadRecord<DocumentRecord>(captured, DocumentStorageKeys.RecordKey(target));
        if (vector is null || document is null || document.Revision != vector.DocumentRevision)
        {
            return;
        }
        var lineage = budget.ReadRecord<VectorProjectionLineage>(captured,
            VectorProjectionKeys.Lineage(target.Partition, target.Collection, field, target.Id));
        if (lineage is null)
        {
            return;
        }
        VectorProjectionEligibility.ValidateLineage(lineage, target.Partition, document, vector);
        database.Authorization.Require(principal, target.Partition, lineage.SourceDocument.Collection, Capability.DocumentsRead);
        var sourceResource = ResourceExecutionQueriesResourceLifetimeReadBinding.BindAuthorizedReadResource(budget.CreateView(current), target.Partition,
            lineage.SourceDocument.Collection, ResourceKind.Collection);
        database.Authorization.RequireFieldUse(principal, sourceResource, lineage.InputField);
        var originalResource = budget.ReadRecord<ResourceDefinition>(captured,
            KeySpace.Resource(target.Partition.TenantId, target.Partition.DatabaseId, lineage.SourceDocument.Collection))
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing);
        if (ResourceHash(sourceResource, budget) != ResourceHash(originalResource, budget))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Changed);
        }
        RequireCurrentRow(database, current, principal, lineage.SourceDocument, budget);
    }

    private static void RequireCurrentRow(DatabaseEngine database, IKeyValueView current, PrincipalRecord principal,
        EntityRef reference, ReadExecutionBudget budget)
    {
        var row = budget.ReadRecord<DocumentRecord>(current, DocumentStorageKeys.RecordKey(reference))
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing);
        if (!database.Authorization.CanReadRow(principal, row.Access))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, Changed);
        }
    }

    private static string ResourceHash(ResourceDefinition resource, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(OrleansSerialization.Measure(resource));
        return Convert.ToHexStringLower(SHA256.HashData(OrleansSerialization.Serialize(resource)));
    }
}
