using KeyLoad.Core.Features.RelationalStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string TableRemovalMissing = "The table resource is not configured.";
    private const string TableRemovalKind = "The resource is not a canonical typed table.";

    private OperationResult ExecuteDropTable(IAtomicTransaction transaction, ReplicatedOperation operation,
        long position)
    {
        var request = Payload<DropTableRequest>(operation);
        JsonData.Identifier(request.TenantId);
        JsonData.Identifier(request.DatabaseId);
        JsonData.Identifier(request.ResourceName);
        var key = KeySpace.Resource(request.TenantId, request.DatabaseId, request.ResourceName);
        var definition = transaction.GetRecord<ResourceDefinition>(key)
            ?? throw Errors.Fail(ErrorCode.NotFound, TableRemovalMissing);
        var lifetime = ResourceLifetimeCatalog.Read(transaction, request.TenantId, request.DatabaseId,
            request.ResourceName, definition)
            ?? throw Errors.Fail(ErrorCode.Corruption, TableRemovalMissing);
        if (definition.Kind != ResourceKind.Collection || definition.Authority != DocumentAuthority.Document
            || definition.RelationalSchema is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TableRemovalKind);
        }
        var work = new ReadExecutionBudget(OperationLimitsOptions, EvaluationClock);
        var plan = TableDataCleanup.Capture(transaction, lifetime, work, Limits);
        foreach (var retained in plan)
        {
            transaction.Delete(retained);
        }
        transaction.Delete(key);
        transaction.Delete(ResourceLifetimeCatalog.Key(request.TenantId, request.DatabaseId, request.ResourceName));
        var token = new CommitToken(Store.Identity.Incarnation, NativeCatalogRoute.PartitionId, position);
        return Result(new DropTableResult(request.TenantId, request.DatabaseId, request.ResourceName,
            new(operation.Id, token, [], Durability, AcknowledgedPersistedCopies)));
    }
}
