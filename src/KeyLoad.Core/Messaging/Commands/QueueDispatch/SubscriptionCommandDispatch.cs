using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes SubscriptionCommandDispatch operations against the caller's database context.</summary>
public static class SubscriptionCommandDispatch
{
    internal static SubscriptionInfo ExecuteConfigureSubscription(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<ConfigureSubscriptionRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return engine.ConfigureSubscription(transaction, principal, request, operation.Timestamp);
    }

    internal static SubscriptionInfo ExecuteSeekSubscription(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<SeekSubscriptionRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return engine.SeekSubscription(transaction, principal, request, operation.Timestamp);
    }

    internal static SubscriptionInfo ExecutePauseSubscription(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation)
    {
        var request = OperationProtocol.Payload<SetSubscriptionPausedRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return engine.SetSubscriptionPaused(transaction, request);
    }

    internal static ReceiveSubscriptionResult ExecuteReceiveSubscription(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position)
    {
        var request = OperationProtocol.Payload<ReceiveSubscriptionRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.RequestId);
        return engine.ReceiveSubscription(transaction, principal, request, operation.Timestamp, position);
    }

    internal static CommitReceipt ExecuteSubscriptionDelivery(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position)
    {
        var request = OperationProtocol.Payload<SubscriptionDeliveryCommand>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return engine.CompleteSubscriptionDelivery(transaction, principal, request, operation.Timestamp, position);
    }

    internal static SubscriptionProcessingResult ExecuteSubscriptionProcessing(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position)
    {
        var request = OperationProtocol.Payload<SubscriptionProcessingRequest>(operation);
        OperationProtocol.RequireEnvelopeId(operation, request.CommandId);
        return engine.CompleteSubscriptionProcessing(transaction, principal, request, operation.Timestamp, position);
    }
}
