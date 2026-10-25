using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class PublicReadResponse
{
    internal static global::ManagedCode.Communication.Result<object?> Create(GrainReadKind? kind, OperationKind? commandKind, object? value)
    {
        if (commandKind != OperationKind.ConfigureApiKey)
        {
            return ((object?)((Create(kind, value)).Value)) is { } outcomeValue1 ? global::ManagedCode.Communication.Result<object?>.Succeed(outcomeValue1) : global::ManagedCode.Communication.Result<object?>.FailNotFound(nameof(Create));
        }
        return value is true ? global::ManagedCode.Communication.Result<object?>.FailNotFound(nameof(Create)) : throw Errors.Fail(ErrorCode.RecoveryRequired, GrainRoutingProtocol.InvalidRequest);
    }

    internal static global::ManagedCode.Communication.Result<object?> Create(GrainReadKind? kind, object? value) => kind switch
    {
        GrainReadKind.Document => global::ManagedCode.Communication.Result<object?>.Succeed((object?)(new DocumentReadResult((Require<DocumentResult>(value)).Value))),
        GrainReadKind.Message => global::ManagedCode.Communication.Result<object?>.Succeed((object?)(new MessageInspectionResult((Require<MessageInspection>(value)).Value))),
        GrainReadKind.QueueTransfer => global::ManagedCode.Communication.Result<object?>.Succeed((object?)(new QueueTransferReadResult((Require<QueueTransferInspection>(value)).Value))),
        GrainReadKind.QueueTransferReceipt => global::ManagedCode.Communication.Result<object?>.Succeed((object?)(new QueueTransferReceiptReadResult((Require<QueueTransferReceiptInspection>(value)).Value))),
        GrainReadKind.RecurringSchedule => global::ManagedCode.Communication.Result<object?>.Succeed((object?)(new RecurringScheduleReadResult((Require<RecurringScheduleInspection>(value)).Value))),
        GrainReadKind.Saga => global::ManagedCode.Communication.Result<object?>.Succeed((object?)(new SagaReadResult((Require<SagaInspection>(value)).Value))),
        _ => ((object?)(value)) is { } outcomeValue7 ? global::ManagedCode.Communication.Result<object?>.Succeed(outcomeValue7) : global::ManagedCode.Communication.Result<object?>.FailNotFound(nameof(Create))
    };

    private static global::ManagedCode.Communication.Result<T?> Require<T>(object? value) where T : class => value is null ? global::ManagedCode.Communication.Result<T?>.FailNotFound(nameof(Require)) :
        global::ManagedCode.Communication.Result<T?>.Succeed(((T?)(value as T ?? throw Errors.Fail(ErrorCode.RecoveryRequired, GrainRoutingProtocol.InvalidRequest))));
}
