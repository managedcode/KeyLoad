using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes DocumentStorageCommands operations against the caller's database context.</summary>
public static class DocumentStorageCommands
{
    internal const string DirectEventDocumentMutationMessage = "Event-authoritative documents cannot be mutated directly.";
    internal const string PatchOperationBudgetMessage = "The patch exceeds its operation budget.";
    internal const string UnavailableDocumentMutationMessage = "The document is unavailable.";
    internal const string PutDocumentMutationKind = "putDocument";
    internal const string PatchDocumentMutationKind = "patchDocument";
    internal const string DeleteDocumentMutationKind = "deleteDocument";
    internal const string DeletedDocumentJson = "{}";
    internal const int MaximumPatchOperations = 256;
    internal static DocumentMutationResult Put(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, PutDocument put, DateTimeOffset now, DocumentMutationContext context)
    {
        const int ActualEmptyCount = 0;
        const int PreviousRevisionValidationBoundary = 0;
        const int PreviousRevisionStep = 1;
        JsonData.Identifier(put.Id);
        var resource = engine.Resource(tx, partition, put.Collection, ResourceKind.Collection);
        RequireDocumentAuthority(resource);
        var previous = context.Before;
        if (previous is not null)
        {
            EntityReferenceLifetime.RequireStored(tx, previous.Reference, resource);
        }

        if (previous is { Deleted: false })
        {
            engine.Authorization.RequireWriteRow(principal, previous.Access);
        }

        DocumentStorageExecutionDocuments.CheckRevision(previous?.Revision ?? ActualEmptyCount, put.ExpectedRevision);
        if (previous is { Deleted: false })
        {
            engine.Authorization.RequireReplacement(principal, resource, put.ExplicitReplacement);
        }

        RelationalStorage.RelationalRowValidation.ValidateRow(resource, put.Id, put.Json, engine.Limits);
        var json = JsonData.ValidateContent(put.Json, engine.Limits);
        foreach (var policy in resource.FieldPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        var access = put.Access ?? previous?.Access ?? new RowAccess();
        if (access.TeamId is { } team)
        {
            JsonData.Identifier(team);
        }

        engine.Authorization.RequireWriteRow(principal, access);
        var document = new DocumentRecord(EntityReferenceLifetime.Bind(tx, new(partition, put.Collection, put.Id), resource), checked((previous?.Revision ?? PreviousRevisionValidationBoundary) + PreviousRevisionStep), json, access, now);
        engine.UpdateIndexes(tx, principal, resource, previous is { Deleted: false } ? previous : null, document);
        tx.PutRecord(context.Key, document);
        return new(new(PutDocumentMutationKind, put.Collection, put.Id, document.Revision), document);
    }

    internal static DocumentMutationResult Patch(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, PatchDocument patch, DateTimeOffset now, DocumentMutationContext context)
    {
        const int PatchesLengthFirstCount = 1;
        const int RevisionStep = 1;
        if (patch.Patches.Length is < PatchesLengthFirstCount or > MaximumPatchOperations)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, PatchOperationBudgetMessage);
        }

        var resource = engine.Resource(tx, partition, patch.Collection, ResourceKind.Collection);
        RequireDocumentAuthority(resource);
        var previous = context.Before;
        if (previous is not null)
        {
            EntityReferenceLifetime.RequireStored(tx, previous.Reference, resource);
        }

        if (previous is null || previous.Deleted)
        {
            throw Errors.Fail(ErrorCode.NotFound, UnavailableDocumentMutationMessage);
        }

        engine.Authorization.RequireWriteRow(principal, previous.Access);
        DocumentStorageExecutionDocuments.CheckRevision(previous.Revision, patch.ExpectedRevision);
        foreach (var field in patch.Patches)
        {
            engine.Authorization.RequireFieldWrite(principal, resource, field.Path);
        }

        RelationalStorage.RelationalRowValidation.ValidatePatchValues(resource, patch.Patches, engine.Limits);
        var updated = previous with
        {
            Json = JsonData.Patch(previous.Json, patch.Patches, engine.Limits),
            Revision = checked(previous.Revision + RevisionStep),
            UpdatedAt = now
        };
        RelationalStorage.RelationalRowValidation.ValidateRow(resource, patch.Id, updated.Json, engine.Limits);
        engine.UpdateIndexes(tx, principal, resource, previous, updated);
        tx.PutRecord(context.Key, updated);
        return new(new(PatchDocumentMutationKind, patch.Collection, patch.Id, updated.Revision), updated);
    }

    internal static DocumentMutationResult Delete(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, DeleteDocument delete, DateTimeOffset now, DocumentMutationContext context)
    {
        const int RevisionStep = 1;
        var resource = engine.Resource(tx, partition, delete.Collection, ResourceKind.Collection);
        RequireDocumentAuthority(resource);
        var previous = context.Before;
        if (previous is not null)
        {
            EntityReferenceLifetime.RequireStored(tx, previous.Reference, resource);
        }

        if (previous is null || previous.Deleted)
        {
            throw Errors.Fail(ErrorCode.NotFound, UnavailableDocumentMutationMessage);
        }

        engine.Authorization.RequireWriteRow(principal, previous.Access);
        DocumentStorageExecutionDocuments.CheckRevision(previous.Revision, delete.ExpectedRevision);
        engine.UpdateIndexes(tx, principal, resource, previous, null);
        var tombstone = previous with
        {
            Json = DeletedDocumentJson,
            Deleted = true,
            Revision = checked(previous.Revision + RevisionStep),
            UpdatedAt = now
        };
        tx.PutRecord(context.Key, tombstone);
        return new(new(DeleteDocumentMutationKind, delete.Collection, delete.Id, tombstone.Revision), tombstone);
    }

    internal static void RequireDocumentAuthority(ResourceDefinition resource)
    {
        if (resource.Authority != DocumentAuthority.Document)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, DirectEventDocumentMutationMessage);
        }
    }
}
