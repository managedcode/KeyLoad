using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes SubscriptionCommandDispatch operations against the caller's database context.</summary>
public static class SubscriptionCommandDispatch
{
    internal static Result<object> ExecuteConfigureSubscription(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<ConfigureSubscriptionRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return OperationProtocol.Result(engine.ConfigureSubscription(transaction, principal, request, operation.Timestamp));
    }

    internal static Result<object> ExecuteSeekSubscription(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<SeekSubscriptionRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return OperationProtocol.Result(engine.SeekSubscription(transaction, principal, request, operation.Timestamp));
    }

    internal static Result<object> ExecutePauseSubscription(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<SetSubscriptionPausedRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return OperationProtocol.Result(engine.SetSubscriptionPaused(transaction, request));
    }

    internal static Result<object> ExecuteReceiveSubscription(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position)
    {
        var request = OperationProtocol.Payload<ReceiveSubscriptionRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.RequestId);
        return OperationProtocol.Result(engine.ReceiveSubscription(transaction, principal, request, operation.Timestamp, position));
    }

    internal static Result<object> ExecuteSubscriptionDelivery(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position)
    {
        var request = OperationProtocol.Payload<SubscriptionDeliveryCommand>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return OperationProtocol.Result(engine.CompleteSubscriptionDelivery(transaction, principal, request, operation.Timestamp, position));
    }

    internal static Result<object> ExecuteSubscriptionProcessing(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position)
    {
        var request = OperationProtocol.Payload<SubscriptionProcessingRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return OperationProtocol.Result(engine.CompleteSubscriptionProcessing(transaction, principal, request, operation.Timestamp, position));
    }
}
