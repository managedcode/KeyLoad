using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsQueueDispatch operations against the caller's database context.</summary>
public static class MessagingCommandsQueueDispatch
{
    internal static Result<object> ExecuteQueueDispatchControl(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<QueueDispatchControlRequest>(operation);
        JsonData.Identifier(request.TenantId);
        JsonData.Identifier(request.DatabaseId);
        JsonData.Identifier(request.Queue);
        var key = KeySpace.Resource(request.TenantId, request.DatabaseId, request.Queue);
        var current = transaction.GetRecord<ResourceDefinition>(key);
        _ = ResourceLifetimeCatalog.Read(transaction, request.TenantId, request.DatabaseId, request.Queue, current);
        var replacement = QueueDispatchStateUpdates.Apply(current, request.ExpectedSchemaVersion, request.Paused);
        ResourceExecutionCommands.ValidateResource(new(request.TenantId, request.DatabaseId, replacement));
        transaction.PutRecord(key, replacement);
        return OperationProtocol.Result(replacement);
    }
}
