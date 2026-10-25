using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class PublicReadResponse
{
    internal static (bool HasValue, object? Value) Create(GrainReadKind? kind, OperationKind? commandKind, object? value)
    {
        if (commandKind != OperationKind.ConfigureApiKey)
        {
            return ((object?)((Create(kind, value)).Value)) is { } outcomeValue1 ? (true, outcomeValue1) : (false, default(object?));
        }
        return value is true ? (false, default(object?)) : throw Errors.Fail(ErrorCode.RecoveryRequired, GrainRoutingProtocol.InvalidRequest);
    }

    internal static (bool HasValue, object? Value) Create(GrainReadKind? kind, object? value) => kind switch
    {
        GrainReadKind.Document => (true, (object?)(new DocumentReadResult((Require<DocumentResult>(value)).Value))),
        GrainReadKind.Message => (true, (object?)(new MessageInspectionResult((Require<MessageInspection>(value)).Value))),
        GrainReadKind.QueueTransfer => (true, (object?)(new QueueTransferReadResult((Require<QueueTransferInspection>(value)).Value))),
        GrainReadKind.QueueTransferReceipt => (true, (object?)(new QueueTransferReceiptReadResult((Require<QueueTransferReceiptInspection>(value)).Value))),
        GrainReadKind.RecurringSchedule => (true, (object?)(new RecurringScheduleReadResult((Require<RecurringScheduleInspection>(value)).Value))),
        GrainReadKind.Saga => (true, (object?)(new SagaReadResult((Require<SagaInspection>(value)).Value))),
        _ => ((object?)(value)) is { } outcomeValue7 ? (true, outcomeValue7) : (false, default(object?))
    };

    private static (bool HasValue, T? Value) Require<T>(object? value) where T : class => value is null ? (false, default(T?)) :
        (true, ((T?)(value as T ?? throw Errors.Fail(ErrorCode.RecoveryRequired, GrainRoutingProtocol.InvalidRequest))));
}
