using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

/// <summary>Revalidates the current source authority for a derived vector inside its read cut.</summary>
internal static class VectorProjectionEligibility
{

    private const string LineageCorrupt = "The stored vector projection lineage is corrupt.";

    internal static bool IsEligible(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, DocumentRecord targetDocument, VectorRecord vector,
        ReadExecutionBudget budget, AnnLifetimeResourceResolver? lifetime = null, bool sourcePolicies = false)
    {
        if (vector.DocumentId is null || vector.Field is null || vector.Space is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        }
        var lineage = budget.ReadRecord<VectorProjectionLineage>(view,
            VectorProjectionKeys.Lineage(partition, collection, vector.Field, vector.DocumentId));
        if (lineage is null)
        {
            return true;
        }

        ValidateLineage(lineage, partition, targetDocument, vector);
        VectorProjectionPolicy.ValidateEffect(budget.ReadRecord<VectorProjectionEffect>(view,
            VectorProjectionKeys.Effect(partition, lineage)), lineage, vector);
        var sourceResource = budget.ReadRecord<ResourceDefinition>(view,
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, lineage.SourceDocument.Collection));
        if (sourceResource is null)
        {
            return false;
        }
        VectorProjectionLineageValidation.ValidateResource(sourceResource, lineage.SourceDocument.Collection, partition);
        if (!CanReadSource(database, principal, partition, sourceResource))
        {
            return false;
        }
        var metadata = budget.CreateView(view);
        _ = DatabaseEngine.BindAuthorizedReadResource(metadata, partition,
            sourceResource.Name, ResourceKind.Collection);
        EntityReferenceLifetime.RequireStored(metadata, lineage.SourceDocument, sourceResource);
        if (lifetime is not null)
        {
            var sourceDefinition = lifetime.Resolve(database, view, partition, sourceResource, budget);
            if (sourceDefinition is null || !lifetime.CanSeeInput(database, sourceResource, sourceDefinition, lineage.InputField))
            {
                return false;
            }
            sourceResource = sourcePolicies ? sourceDefinition : sourceResource;
        }
        var source = budget.ReadRecord<DocumentRecord>(view,
            DocumentStorageKeys.RecordKey(partition, lineage.SourceDocument.Collection, lineage.SourceDocument.Id));
        if (source is null || source.Deleted || source.Revision != lineage.SourceDocumentRevision)
        {
            return false;
        }
        EntityReferenceLifetime.RequireStored(metadata, source.Reference, sourceResource);
        if (source.Reference != lineage.SourceDocument)
        {
            throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        }
        if (!database.Authorization.CanReadRow(principal, source.Access)
            || !CanUseSourceField(database, principal, sourceResource, lineage.InputField))
        {
            return false;
        }

        return HasCompatiblePolicy(database, view, partition, collection, lineage, sourceResource, budget, lifetime, sourcePolicies);
    }

    private static bool HasCompatiblePolicy(DatabaseEngine database, IKeyValueView view, PartitionRef partition,
        string collection, VectorProjectionLineage lineage, ResourceDefinition sourceResource,
        ReadExecutionBudget budget, AnnLifetimeResourceResolver? lifetime, bool sourcePolicies)
    {
        var targetResource = budget.ReadRecord<ResourceDefinition>(view,
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, collection))
            ?? throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        VectorProjectionLineageValidation.ValidateResource(targetResource, collection, partition);
        if (lifetime is not null)
        {
            var definition = lifetime.Resolve(database, view, partition, targetResource, budget);
            if (definition is null)
            {
                return false;
            }
            targetResource = sourcePolicies ? definition : targetResource;
        }
        var sourceClasses = VectorProjectionPolicy.Classifications(database.Authorization, sourceResource,
            lineage.InputField);
        var targetClasses = VectorProjectionPolicy.Classifications(database.Authorization, targetResource,
            lineage.TargetField);
        return VectorProjectionPolicy.Same(sourceClasses, lineage.SourceClassifications)
            && VectorProjectionPolicy.Same(targetClasses, lineage.TargetClassifications)
            && VectorProjectionPolicy.TargetIsAtLeastAsRestrictive(sourceClasses, targetClasses);
    }

    private static bool CanReadSource(DatabaseEngine database, PrincipalRecord principal, PartitionRef partition,
        ResourceDefinition resource)
    {
        try
        {
            database.Authorization.Require(principal, partition, resource.Name, Capability.DocumentsRead);
            return true;
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.PermissionDenied)
        {
            return false;
        }
    }

    private static bool CanUseSourceField(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, string field)
    {
        try
        {
            database.Authorization.RequireFieldUse(principal, resource, field);
            return true;
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.PermissionDenied)
        {
            return false;
        }
    }

    internal static void ValidateLineage(VectorProjectionLineage lineage, PartitionRef partition,
        DocumentRecord targetDocument, VectorRecord vector)
        => VectorProjectionLineageValidation.ValidateLineage(lineage, partition, targetDocument, vector);
}
