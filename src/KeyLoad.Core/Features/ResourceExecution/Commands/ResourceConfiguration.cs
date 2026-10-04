using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int MaxResourceIndexes = 32;
    private const int MaxResourceFieldPolicies = 256;
    private const int MaxIndexFields = 8;
    private const int MaxConfiguredQueueLeaseSeconds = 3_600;
    private const string InvalidResourceSchemaMessage = "The resource schema contains an invalid entry.";
    private const string ResourceSchemaBudgetMessage = "The resource schema exceeds its budget.";
    private const string DuplicateIndexNamesMessage = "Index names must be unique.";
    private const string InvalidIndexFieldsMessage = "An index requires one to eight fields.";
    private const string InvalidEventQuotaMessage = "The retained event quota is invalid.";
    private const string InvalidQueuePolicyMessage = "The queue policy is invalid.";

    private OperationResult ExecuteConfigureResource(IAtomicTransaction transaction, ReplicatedOperation operation)
    {
        var request = Payload<ConfigureResourceRequest>(operation);
        ValidateResource(request);
        var key = KeySpace.Resource(request.TenantId, request.DatabaseId, request.Definition.Name);
        var previous = transaction.GetRecord<ResourceDefinition>(key);
        ResourcePolicyUpdates.Validate(previous, request.Definition, request.ExpectedSchemaVersion);
        BlobStorageOperations.ConfigureResource(transaction, request, previous is null, Store.Identity.Incarnation);
        transaction.PutRecord(key, request.Definition);
        return Result(request.Definition);
    }

    private static void ValidateResource(ConfigureResourceRequest request)
    {
        JsonData.Identifier(request.TenantId);
        JsonData.Identifier(request.DatabaseId);
        JsonData.Identifier(request.Definition.Name);
        JsonData.Identifier(request.Definition.TransactionDomainId);
        var definition = request.Definition;
        Features.RelationalStorage.RelationalRowValidation.ValidateSchema(definition);
        BlobStorageOperations.ValidatePolicy(definition);
        if (!Enum.IsDefined(definition.Kind) || !Enum.IsDefined(definition.Authority)
            || definition.Indexes.Any(index => index is null || index.Fields.Any(string.IsNullOrEmpty))
            || definition.FieldPolicies.Concat(definition.HeaderPolicies).Any(policy => policy is null || string.IsNullOrEmpty(policy.Path)))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidResourceSchemaMessage);
        }

        if (definition.Indexes.Length > MaxResourceIndexes || definition.FieldPolicies.Length > MaxResourceFieldPolicies
            || definition.HeaderPolicies.Length > MaxResourceFieldPolicies)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceSchemaBudgetMessage);
        }

        if (definition.Indexes.Select(i => i.Name).Distinct(StringComparer.Ordinal).Count() != definition.Indexes.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, DuplicateIndexNamesMessage);
        }

        ValidateResourcePaths(definition);
        ValidateResourcePolicies(definition);
    }

    private static void ValidateResourcePaths(ResourceDefinition definition)
    {
        foreach (var index in definition.Indexes)
        {
            JsonData.Identifier(index.Name);
            if (index.Fields.Length is < 1 or > MaxIndexFields)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidIndexFieldsMessage);
            }

            foreach (var field in index.Fields)
            {
                JsonData.PathSegments(field);
            }
        }
        foreach (var policy in definition.FieldPolicies.Concat(definition.HeaderPolicies))
        {
            JsonData.PathSegments(policy.Path);
        }
    }

    private static void ValidateResourcePolicies(ResourceDefinition definition)
    {
        var q = definition.QueuePolicy;
        if (definition.EventRetention.MaxEvents < 1 || definition.EventRetention.MaxBytes < 1)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidEventQuotaMessage);
        }

        if (q.MaxAttempts < 1 || q.MaxLeaseSeconds is < 1 or > MaxConfiguredQueueLeaseSeconds || q.MaxStoredMessages < 1 || q.MaxStoredBytes < 1
            || q.MaxInFlightMessages < 1 || q.MaxInFlightBytes < 1 || q.RetryBaseMilliseconds < 1 || q.RetryMaxMilliseconds < q.RetryBaseMilliseconds)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidQueuePolicyMessage);
        }
    }

}
