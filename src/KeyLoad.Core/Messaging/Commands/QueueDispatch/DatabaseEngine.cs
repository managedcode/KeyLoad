using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static OperationResult ExecuteQueueDispatchControl(IAtomicTransaction transaction, ReplicatedOperation operation)
    {
        var request = Payload<QueueDispatchControlRequest>(operation);
        JsonData.Identifier(request.TenantId);
        JsonData.Identifier(request.DatabaseId);
        JsonData.Identifier(request.Queue);
        var key = KeySpace.Resource(request.TenantId, request.DatabaseId, request.Queue);
        var current = transaction.GetRecord<ResourceDefinition>(key);
        _ = ResourceLifetimeCatalog.Read(transaction, request.TenantId, request.DatabaseId, request.Queue, current);
        var replacement = QueueDispatchStateUpdates.Apply(current, request.ExpectedSchemaVersion, request.Paused);
        ValidateResource(new(request.TenantId, request.DatabaseId, replacement));
        RequireResourceMetadataChangeAdmission(transaction, request.TenantId, request.DatabaseId);
        transaction.PutRecord(key, replacement);
        return Result(replacement);
    }
}
