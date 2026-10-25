using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes TypedRowInsertCommands operations against the caller's database context.</summary>
public static class TypedRowInsertCommands
{
    internal static PutDocument ExpandTypedRowInsert(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, PartitionRef partition, InsertTypedRow insert)
    {
        var resource = engine.Resource(transaction, partition, insert.Collection, ResourceKind.Collection);
        engine.RequireTypedInsertPermissions(principal, partition, resource);
        var id = RelationalStorage.RelationalRowValidation.InsertIdentity(resource, insert.Json);
        if (transaction.GetRecord<DocumentRecord>(DocumentStorageExecutionDocuments.DocumentKey(partition, insert.Collection, id)) is { } previous)
        {
            engine.Authorization.RequireWriteRow(principal, previous.Access);
            throw Errors.Fail(ErrorCode.Conflict, ExistingInsertedRowMessage);
        }

        return new(insert.Collection, id, insert.Json, RelationalInsertProtocol.AbsentRevision);
    }

    internal const string ExistingInsertedRowMessage = "The inserted canonical row already exists.";
}
