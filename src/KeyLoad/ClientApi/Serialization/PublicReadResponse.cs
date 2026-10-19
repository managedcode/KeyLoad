using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class PublicReadResponse
{
    internal static object? Create(GrainReadKind? kind, OperationKind? commandKind, object? value)
    {
        if (commandKind != OperationKind.ConfigureApiKey)
        {
            return Create(kind, value);
        }
        return value is true ? null : throw Errors.Fail(ErrorCode.RecoveryRequired, GrainRoutingProtocol.InvalidRequest);
    }

    internal static object? Create(GrainReadKind? kind, object? value) => kind switch
    {
        GrainReadKind.Document => new DocumentReadResult(Require<DocumentResult>(value)),
        GrainReadKind.Message => new MessageInspectionResult(Require<MessageInspection>(value)),
        GrainReadKind.QueueTransfer => new QueueTransferReadResult(Require<QueueTransferInspection>(value)),
        GrainReadKind.QueueTransferReceipt => new QueueTransferReceiptReadResult(Require<QueueTransferReceiptInspection>(value)),
        GrainReadKind.RecurringSchedule => new RecurringScheduleReadResult(Require<RecurringScheduleInspection>(value)),
        GrainReadKind.Saga => new SagaReadResult(Require<SagaInspection>(value)),
        _ => value
    };

    private static T? Require<T>(object? value) where T : class => value is null ? null :
        value as T ?? throw Errors.Fail(ErrorCode.RecoveryRequired, GrainRoutingProtocol.InvalidRequest);
}
