using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private Result<object> ExecuteConfigureProjection(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = Payload<ConfigureProjectionConsumerRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        var authority = CaptureProjectionMaintenanceAuthority(transaction, request, operation);
        return Result(ConfigureProjectionConsumer(transaction, request, authority));
    }

    private Result<object> ExecuteProjectionBatch(IAtomicTransaction transaction, PrincipalRecord principal,
        Command<DatabaseCommand> operation, long position)
    {
        var request = Payload<CommitProjectionBatchRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(CommitProjectionBatch(transaction, principal, request, operation.Timestamp, position));
    }

    private static Result<object> ExecuteReleaseProjection(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = Payload<ReleaseProjectionConsumerRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(ReleaseProjectionConsumer(transaction, request));
    }

    private Result<object> ExecutePurgeOutbox(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = Payload<PurgeOutboxRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(PurgeOutbox(transaction, request));
    }
}
