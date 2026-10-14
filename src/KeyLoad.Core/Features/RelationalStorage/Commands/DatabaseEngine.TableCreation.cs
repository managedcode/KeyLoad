using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string TableCreationInvalid = "The canonical table definition is invalid.";
    private const string TableCreationExists = "The table resource already exists.";

    private static OperationResult ExecuteCreateTable(IAtomicTransaction transaction, ReplicatedOperation operation)
    {
        var request = Payload<CreateTableRequest>(operation);
        if (request is null || request.Definition is null
            || request.Definition.Kind != ResourceKind.Collection
            || request.Definition.Authority != DocumentAuthority.Document
            || request.Definition.RelationalSchema is null)
        { throw Errors.Fail(ErrorCode.Validation, TableCreationInvalid); }
        var configuration = new ConfigureResourceRequest(request.TenantId, request.DatabaseId, request.Definition);
        ValidateResource(configuration);
        var key = KeySpace.Resource(request.TenantId, request.DatabaseId, request.Definition.Name);
        if (transaction.GetRecord<ResourceDefinition>(key) is not null)
        { throw Errors.Fail(ErrorCode.Conflict, TableCreationExists); }
        RequireResourceMetadataChangeAdmission(transaction, request.TenantId, request.DatabaseId);
        transaction.PutRecord(key, request.Definition);
        return Result(request.Definition);
    }
}
