using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes RelationalStorageCommands operations against the caller's database context.</summary>
public static class RelationalStorageCommands
{
    internal const string TableCreationInvalid = "The canonical table definition is invalid.";
    internal const string TableCreationExists = "The table resource already exists.";
    internal static Result<object> ExecuteCreateTable(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<CreateTableRequest>(operation);
        if (request is null || request.Definition is null || request.Definition.Kind != ResourceKind.Collection || request.Definition.Authority != DocumentAuthority.Document || request.Definition.RelationalSchema is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TableCreationInvalid);
        }

        var configuration = new ConfigureResourceRequest(request.TenantId, request.DatabaseId, request.Definition);
        ResourceExecutionCommands.ValidateResource(configuration);
        var key = KeySpace.Resource(request.TenantId, request.DatabaseId, request.Definition.Name);
        var previous = transaction.GetRecord<ResourceDefinition>(key);
        _ = ResourceLifetimeCatalog.Read(transaction, request.TenantId, request.DatabaseId, request.Definition.Name, previous);
        if (previous is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, TableCreationExists);
        }

        _ = ResourceLifetimeCatalog.Create(transaction, operation, request.TenantId, request.DatabaseId, request.Definition);
        transaction.PutRecord(key, request.Definition);
        return OperationProtocol.Result(request.Definition);
    }
}
