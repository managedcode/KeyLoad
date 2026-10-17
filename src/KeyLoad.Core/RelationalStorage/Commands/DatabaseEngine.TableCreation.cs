using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string TableCreationInvalid = "The canonical table definition is invalid.";
    private const string TableCreationExists = "The table resource already exists.";

    private OperationResult ExecuteCreateTable(IAtomicTransaction transaction, ReplicatedOperation operation)
    {
        var request = Payload<CreateTableRequest>(operation);
        if (request is null || request.Definition is null
            || request.Definition.Kind != ResourceKind.Collection
            || request.Definition.Authority != DocumentAuthority.Document
            || request.Definition.RelationalSchema is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TableCreationInvalid);
        }
        var configuration = new ConfigureResourceRequest(request.TenantId, request.DatabaseId, request.Definition);
        ValidateResource(configuration);
        var key = KeySpace.Resource(request.TenantId, request.DatabaseId, request.Definition.Name);
        var previous = transaction.GetRecord<ResourceDefinition>(key);
        _ = ResourceLifetimeCatalog.Read(transaction, request.TenantId, request.DatabaseId, request.Definition.Name, previous);
        if (previous is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, TableCreationExists);
        }
        RequireResourceMetadataChangeAdmission(transaction, request.TenantId, request.DatabaseId);
        _ = ResourceLifetimeCatalog.Create(transaction, operation, request.TenantId, request.DatabaseId, request.Definition, Limits);
        transaction.PutRecord(key, request.Definition);
        return Result(request.Definition);
    }
}
