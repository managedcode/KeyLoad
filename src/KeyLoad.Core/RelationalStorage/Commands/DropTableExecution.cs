using KeyLoad.Core.Features.RelationalStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes DropTableExecution operations against the caller's database context.</summary>
public static class DropTableExecution
{
    internal const string TableRemovalMissing = "The table resource is not configured.";
    internal const string TableRemovalKind = "The resource is not a canonical typed table.";
    internal static Result<object> ExecuteDropTable(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation, long position, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = OperationProtocol.Payload<DropTableRequest>(operation);
        JsonData.Identifier(request.TenantId);
        JsonData.Identifier(request.DatabaseId);
        JsonData.Identifier(request.ResourceName);
        var key = KeySpace.Resource(request.TenantId, request.DatabaseId, request.ResourceName);
        var definition = transaction.GetRecord<ResourceDefinition>(key) ?? throw Errors.Fail(ErrorCode.NotFound, TableRemovalMissing);
        var lifetime = ResourceLifetimeCatalog.Read(transaction, request.TenantId, request.DatabaseId, request.ResourceName, definition) ?? throw Errors.Fail(ErrorCode.Corruption, TableRemovalMissing);
        if (definition.Kind != ResourceKind.Collection || definition.Authority != DocumentAuthority.Document || definition.RelationalSchema is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TableRemovalKind);
        }

        var work = new ReadExecutionBudget(engine.OperationLimitsOptions, engine.EvaluationClock, cancellationToken);
        var plan = TableDataCleanup.Capture(transaction, lifetime, work, engine.Limits);
        foreach (var retained in plan)
        {
            work.Check();
            transaction.Delete(retained);
        }

        work.Check();
        transaction.Delete(key);
        work.Check();
        transaction.Delete(ResourceLifetimeCatalog.Key(request.TenantId, request.DatabaseId, request.ResourceName));
        var token = new CommitToken(engine.Store.Identity.Incarnation, CatalogRoute.PartitionId, position);
        return OperationProtocol.Result(new DropTableResult(request.TenantId, request.DatabaseId, request.ResourceName, new(operation.CommandId, token, [], engine.Durability, engine.AcknowledgedPersistedCopies)));
    }
}
