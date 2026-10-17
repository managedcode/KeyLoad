using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteConfigureSubscription(IAtomicTransaction transaction, PrincipalRecord principal, ReplicatedOperation operation)
    {
        var request = Payload<ConfigureSubscriptionRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(ConfigureSubscription(transaction, principal, request, operation.EvaluatedAt));
    }

    private OperationResult ExecuteSeekSubscription(IAtomicTransaction transaction, PrincipalRecord principal, ReplicatedOperation operation)
    {
        var request = Payload<SeekSubscriptionRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(SeekSubscription(transaction, principal, request, operation.EvaluatedAt));
    }

    private OperationResult ExecutePauseSubscription(IAtomicTransaction transaction, ReplicatedOperation operation)
    {
        var request = Payload<SetSubscriptionPausedRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(SetSubscriptionPaused(transaction, request));
    }

    private OperationResult ExecuteReceiveSubscription(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, long position)
    {
        var request = Payload<ReceiveSubscriptionRequest>(operation);
        RequireEnvelopeId(operation, request.RequestId);
        return Result(ReceiveSubscription(transaction, principal, request, operation.EvaluatedAt, position));
    }

    private OperationResult ExecuteSubscriptionDelivery(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, long position)
    {
        var request = Payload<SubscriptionDeliveryCommand>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(CompleteSubscriptionDelivery(transaction, principal, request, operation.EvaluatedAt, position));
    }

    private OperationResult ExecuteSubscriptionProcessing(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, long position)
    {
        var request = Payload<SubscriptionProcessingRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(CompleteSubscriptionProcessing(transaction, principal, request, operation.EvaluatedAt, position));
    }
}
