using KeyLoad.Storage;
namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PutDocument ExpandTypedRowInsert(IAtomicTransaction transaction, PrincipalRecord principal,
        PartitionRef partition, InsertTypedRow insert)
    {
        var resource = Resource(transaction, partition, insert.Collection, ResourceKind.Collection);
        RequireTypedInsertPermissions(principal, partition, resource);
        var id = Features.RelationalStorage.RelationalRowValidation.InsertIdentity(resource, insert.Json, OperationLimitsOptions);
        if (transaction.GetRecord<DocumentRecord>(DocumentKey(partition, insert.Collection, id)) is { } previous)
        {
            Authorization.RequireWriteRow(principal, previous.Access);
            throw Errors.Fail(ErrorCode.Conflict, ExistingInsertedRowMessage);
        }
        return new(insert.Collection, id, insert.Json, RelationalInsertProtocol.AbsentRevision);
    }
    private const string ExistingInsertedRowMessage = "The inserted canonical row already exists.";
}
