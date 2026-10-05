using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Applies document mutations using the current transaction's decoded prior image.</summary>
public sealed partial class DatabaseEngine
{
    private const string DirectEventDocumentMutationMessage = "Event-authoritative documents cannot be mutated directly.";
    private const string PatchOperationBudgetMessage = "The patch exceeds its operation budget.";
    private const string UnavailableDocumentMutationMessage = "The document is unavailable.";
    private const string PutDocumentMutationKind = "putDocument";
    private const string PatchDocumentMutationKind = "patchDocument";
    private const string DeleteDocumentMutationKind = "deleteDocument";
    private const string DeletedDocumentJson = "{}";
    private const int MaximumPatchOperations = 256;

    private DocumentMutationResult Put(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition,
        PutDocument put, DateTimeOffset now, DocumentMutationContext context)
    {
        JsonData.Identifier(put.Id);
        var resource = Resource(tx, partition, put.Collection, ResourceKind.Collection);
        RequireDocumentAuthority(resource);
        var previous = context.Before;
        CheckRevision(previous?.Revision ?? 0, put.ExpectedRevision);
        if (previous is { Deleted: false })
        {
            Authorization.RequireWriteRow(principal, previous.Access);
            Authorization.RequireReplacement(principal, resource, put.ExplicitReplacement);
        }

        Features.RelationalStorage.RelationalRowValidation.ValidateRow(resource, put.Id, put.Json, Limits);
        var json = JsonData.ValidateDocument(put.Json, Limits);
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        var access = put.Access ?? previous?.Access ?? new RowAccess();
        Authorization.RequireWriteRow(principal, access);
        var document = new DocumentRecord(new(partition, put.Collection, put.Id),
            checked((previous?.Revision ?? 0) + 1), json, access, now);
        UpdateIndexes(tx, principal, resource, previous is { Deleted: false } ? previous : null, document);
        tx.PutRecord(context.Key, document);
        return new(new(PutDocumentMutationKind, put.Collection, put.Id, document.Revision), document);
    }

    private DocumentMutationResult Patch(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition,
        PatchDocument patch, DateTimeOffset now, DocumentMutationContext context)
    {
        if (patch.Patches.Length is < 1 or > MaximumPatchOperations)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, PatchOperationBudgetMessage);
        }

        var resource = Resource(tx, partition, patch.Collection, ResourceKind.Collection);
        RequireDocumentAuthority(resource);
        var previous = context.Before;
        if (previous is null || previous.Deleted)
        {
            throw Errors.Fail(ErrorCode.NotFound, UnavailableDocumentMutationMessage);
        }

        CheckRevision(previous.Revision, patch.ExpectedRevision);
        Authorization.RequireWriteRow(principal, previous.Access);
        foreach (var field in patch.Patches)
        {
            Authorization.RequireFieldWrite(principal, resource, field.Path);
        }

        Features.RelationalStorage.RelationalRowValidation.ValidatePatchValues(resource, patch.Patches, Limits);
        var updated = previous with
        {
            Json = JsonData.Patch(previous.Json, patch.Patches, Limits),
            Revision = checked(previous.Revision + 1),
            UpdatedAt = now
        };
        Features.RelationalStorage.RelationalRowValidation.ValidateRow(resource, patch.Id, updated.Json, Limits);
        UpdateIndexes(tx, principal, resource, previous, updated);
        tx.PutRecord(context.Key, updated);
        return new(new(PatchDocumentMutationKind, patch.Collection, patch.Id, updated.Revision), updated);
    }

    private DocumentMutationResult Delete(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition,
        DeleteDocument delete, DateTimeOffset now, DocumentMutationContext context)
    {
        var resource = Resource(tx, partition, delete.Collection, ResourceKind.Collection);
        RequireDocumentAuthority(resource);
        var previous = context.Before;
        if (previous is null || previous.Deleted)
        {
            throw Errors.Fail(ErrorCode.NotFound, UnavailableDocumentMutationMessage);
        }

        CheckRevision(previous.Revision, delete.ExpectedRevision);
        Authorization.RequireWriteRow(principal, previous.Access);
        UpdateIndexes(tx, principal, resource, previous, null);
        var tombstone = previous with
        {
            Json = DeletedDocumentJson,
            Deleted = true,
            Revision = checked(previous.Revision + 1),
            UpdatedAt = now
        };
        tx.PutRecord(context.Key, tombstone);
        return new(new(DeleteDocumentMutationKind, delete.Collection, delete.Id, tombstone.Revision), tombstone);
    }

    private static void RequireDocumentAuthority(ResourceDefinition resource)
    {
        if (resource.Authority != DocumentAuthority.Document)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, DirectEventDocumentMutationMessage);
        }
    }
}
