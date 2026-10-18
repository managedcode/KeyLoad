using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private Result<object> ExecuteConfigureSubscription(IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var request = Payload<ConfigureSubscriptionRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(ConfigureSubscription(transaction, principal, request, operation.Timestamp));
    }

    private Result<object> ExecuteSeekSubscription(IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var request = Payload<SeekSubscriptionRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(SeekSubscription(transaction, principal, request, operation.Timestamp));
    }

    private Result<object> ExecutePauseSubscription(IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = Payload<SetSubscriptionPausedRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(SetSubscriptionPaused(transaction, request));
    }

    private Result<object> ExecuteReceiveSubscription(IAtomicTransaction transaction, PrincipalRecord principal,
        Command<DatabaseCommand> operation, long position)
    {
        var request = Payload<ReceiveSubscriptionRequest>(operation);
        RequireEnvelopeId(operation, request.RequestId);
        return Result(ReceiveSubscription(transaction, principal, request, operation.Timestamp, position));
    }

    private Result<object> ExecuteSubscriptionDelivery(IAtomicTransaction transaction, PrincipalRecord principal,
        Command<DatabaseCommand> operation, long position)
    {
        var request = Payload<SubscriptionDeliveryCommand>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(CompleteSubscriptionDelivery(transaction, principal, request, operation.Timestamp, position));
    }

    private Result<object> ExecuteSubscriptionProcessing(IAtomicTransaction transaction, PrincipalRecord principal,
        Command<DatabaseCommand> operation, long position)
    {
        var request = Payload<SubscriptionProcessingRequest>(operation);
        RequireEnvelopeId(operation, request.CommandId);
        return Result(CompleteSubscriptionProcessing(transaction, principal, request, operation.Timestamp, position));
    }
}
