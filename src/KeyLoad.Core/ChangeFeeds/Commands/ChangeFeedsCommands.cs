using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ChangeFeedsCommands operations against the caller's database context.</summary>
public static class ChangeFeedsCommands
{
    internal static Result<object> ExecuteConfigureProjection(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<ConfigureProjectionConsumerRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        var authority = engine.CaptureProjectionMaintenanceAuthority(transaction, request, operation);
        return OperationProtocol.Result(engine.ConfigureProjectionConsumer(transaction, request, authority));
    }

    internal static Result<object> ExecuteProjectionBatch(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position)
    {
        var request = OperationProtocol.Payload<CommitProjectionBatchRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return OperationProtocol.Result(engine.CommitProjectionBatch(transaction, principal, request, operation.Timestamp, position));
    }

    internal static Result<object> ExecuteReleaseProjection(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<ReleaseProjectionConsumerRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return OperationProtocol.Result(ChangeFeedsExecutionProjectionOutbox.ReleaseProjectionConsumer(transaction, request));
    }

    internal static Result<object> ExecutePurgeOutbox(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<PurgeOutboxRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return OperationProtocol.Result(engine.PurgeOutbox(transaction, request));
    }
}
