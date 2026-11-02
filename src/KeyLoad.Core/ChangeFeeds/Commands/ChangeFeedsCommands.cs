using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ChangeFeedsCommands operations against the caller's database context.</summary>
public static class ChangeFeedsCommands
{
    internal static ProjectionConsumerInfo ExecuteConfigureProjection(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<ConfigureProjectionConsumerRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        var authority = (engine.CaptureProjectionMaintenanceAuthority(transaction, request, operation)).Value;
        return engine.ConfigureProjectionConsumer(transaction, request, authority);
    }

    internal static ProjectionBatchResult ExecuteProjectionBatch(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position)
    {
        var request = OperationProtocol.Payload<CommitProjectionBatchRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return engine.CommitProjectionBatch(transaction, principal, request, operation.Timestamp, position);
    }

    internal static ProjectionConsumerInfo ExecuteReleaseProjection(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<ReleaseProjectionConsumerRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return ProjectionOutbox.ReleaseProjectionConsumer(transaction, request);
    }

    internal static OutboxHead ExecutePurgeOutbox(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<PurgeOutboxRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return engine.PurgeOutbox(transaction, request);
    }
}
