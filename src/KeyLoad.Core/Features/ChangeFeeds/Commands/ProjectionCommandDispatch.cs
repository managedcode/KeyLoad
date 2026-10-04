using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteConfigureProjection(IAtomicTransaction transaction, ReplicatedOperation operation)
    {
        var request = Payload<ConfigureProjectionConsumerRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(ConfigureProjectionConsumer(transaction, request));
    }

    private OperationResult ExecuteProjectionBatch(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, long position)
    {
        var request = Payload<CommitProjectionBatchRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(CommitProjectionBatch(transaction, principal, request, operation.EvaluatedAt, position));
    }

    private static OperationResult ExecuteReleaseProjection(IAtomicTransaction transaction, ReplicatedOperation operation)
    {
        var request = Payload<ReleaseProjectionConsumerRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(ReleaseProjectionConsumer(transaction, request));
    }

    private OperationResult ExecutePurgeOutbox(IAtomicTransaction transaction, ReplicatedOperation operation)
    {
        var request = Payload<PurgeOutboxRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(PurgeOutbox(transaction, request));
    }
}
