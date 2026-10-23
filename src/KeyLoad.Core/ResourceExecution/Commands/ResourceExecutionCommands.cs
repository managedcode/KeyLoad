using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ResourceExecutionCommands operations against the caller's database context.</summary>
public static class ResourceExecutionCommands
{
    internal const int ResourceConfigurationMinimumPositiveCount = 1;
    internal const int MaxResourceIndexes = 32;
    internal const int MaxResourceFieldPolicies = 256;
    internal const int MaxIndexFields = 8;
    internal const int MaxConfiguredQueueLeaseSeconds = 3_600;
    internal const string InvalidResourceSchemaMessage = "The resource schema contains an invalid entry.";
    internal const string ResourceSchemaBudgetMessage = "The resource schema exceeds its budget.";
    internal const string DuplicateIndexNamesMessage = "Index names must be unique.";
    internal const string InvalidIndexFieldsMessage = "An index requires one to eight fields.";
    internal const string InvalidEventQuotaMessage = "The retained event quota is invalid.";
    internal const string InvalidQueuePolicyMessage = "The queue policy is invalid.";
    internal static Result<object> ExecuteConfigureResource(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<ConfigureResourceRequest>(operation);
        ValidateResource(request);
        var key = KeySpace.Resource(request.TenantId, request.DatabaseId, request.Definition.Name);
        var previous = transaction.GetRecord<ResourceDefinition>(key);
        _ = ResourceLifetimeCatalog.Read(transaction, request.TenantId, request.DatabaseId, request.Definition.Name, previous);
        ResourcePolicyUpdates.Validate(previous, request.Definition, request.ExpectedSchemaVersion);
        engine.RequireQueueDeliveryPolicyChange(transaction, request, previous);
        if (previous is null)
        {
            _ = ResourceLifetimeCatalog.Create(transaction, operation, request.TenantId, request.DatabaseId, request.Definition);
        }

        transaction.PutRecord(key, request.Definition);
        return OperationProtocol.Result(request.Definition);
    }

    internal static void ValidateResource(ConfigureResourceRequest request)
    {
        JsonData.Identifier(request.TenantId);
        JsonData.Identifier(request.DatabaseId);
        JsonData.Identifier(request.Definition.Name);
        JsonData.Identifier(request.Definition.TransactionDomainId);
        var definition = request.Definition;
        Features.RelationalStorage.RelationalRowValidation.ValidateSchema(definition);
        Features.AnalyticsStore.AnalyticsStoreValidation.ValidateSchema(definition);
        Features.Search.VectorFieldProfiles.Validate(definition);
        if (!Enum.IsDefined(definition.Kind) || !Enum.IsDefined(definition.Authority) || definition.Indexes.Any(index => index is null || index.Fields.Any(string.IsNullOrEmpty)) || definition.FieldPolicies.Concat(definition.HeaderPolicies).Any(policy => policy is null || policy.Path is null))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidResourceSchemaMessage);
        }

        if (definition.Indexes.Length > MaxResourceIndexes || definition.FieldPolicies.Length > MaxResourceFieldPolicies || definition.HeaderPolicies.Length > MaxResourceFieldPolicies)
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

    internal static void ValidateResourcePaths(ResourceDefinition definition)
    {
        foreach (var index in definition.Indexes)
        {
            JsonData.Identifier(index.Name);
            if (index.Fields.Length is < ResourceConfigurationMinimumPositiveCount or > MaxIndexFields)
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
            if (policy.SchemaReadGrant is { } schemaReadGrant)
            {
                JsonData.Identifier(schemaReadGrant);
            }
        }
    }

    internal static void ValidateResourcePolicies(ResourceDefinition definition)
    {
        TargetInboxValidation.ValidateInboxPolicy(definition);
        if (definition.FilePolicy is null || !definition.FilePolicy.IsValid())
        {
            throw Errors.Fail(ErrorCode.Validation, global::KeyLoad.Core.Features.BlobStorage.FileStorageProtocol.Invalid);
        }

        var q = definition.QueuePolicy;
        if (definition.EventRetention.MaxEvents < ResourceConfigurationMinimumPositiveCount || definition.EventRetention.MaxBytes < ResourceConfigurationMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidEventQuotaMessage);
        }

        if (!Enum.IsDefined(q.OrderingProfile) || !Enum.IsDefined(q.ParkedHeadPolicy) || !Enum.IsDefined(q.RetryJitter) || q.RetryExponentialFactor < ResourceConfigurationMinimumPositiveCount || q.OrderingProfile == QueueOrderingProfile.CompetingConsumers && q.ParkedHeadPolicy == QueueParkedHeadPolicy.Block || q.MaxAttempts < ResourceConfigurationMinimumPositiveCount || q.MaxLeaseSeconds is < ResourceConfigurationMinimumPositiveCount or > MaxConfiguredQueueLeaseSeconds || q.MaxStoredMessages < ResourceConfigurationMinimumPositiveCount || q.MaxStoredBytes < ResourceConfigurationMinimumPositiveCount || q.MaxInFlightMessages < ResourceConfigurationMinimumPositiveCount || q.MaxInFlightBytes < ResourceConfigurationMinimumPositiveCount || q.RetryBaseMilliseconds < ResourceConfigurationMinimumPositiveCount || q.RetryMaxMilliseconds < q.RetryBaseMilliseconds || q.MaxDeadLetterMessages is { } messages && (messages < ResourceConfigurationMinimumPositiveCount || messages > q.MaxStoredMessages) || q.MaxDeadLetterBytes is { } bytes && (bytes < ResourceConfigurationMinimumPositiveCount || bytes > q.MaxStoredBytes))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidQueuePolicyMessage);
        }
    }
}
