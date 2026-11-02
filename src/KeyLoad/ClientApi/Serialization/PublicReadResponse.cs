using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class PublicReadResponse
{
    internal static (bool HasValue, object? Value) /* KLD0041: The terminal public JSON projection contains heterogeneous concrete DTOs and preserves absent versus present-null values without another heap carrier. */ Create(GrainReadKind? kind, OperationKind? commandKind, object? /* KLD0041: Accept only the existing runtime-typed GrainValue reply at the final public JSON projection boundary; concrete output validation follows the selected read kind. */ value)
    {
        if (commandKind != OperationKind.ConfigureApiKey)
        {
            return (Create(kind, value) /* KLD0041: Project the non-status command reply through the same terminal public DTO mapping while preserving ConfigureApiKey true-to-absent semantics. */);
        }
        return value is true ? (false, default(object?)) : throw Errors.Fail(ErrorCode.RecoveryRequired, GrainRoutingProtocol.InvalidRequest);
    }

    internal static (bool HasValue, object? Value) /* KLD0041: The terminal public JSON projection contains heterogeneous concrete DTOs and preserves absent versus present-null values without another heap carrier. */ Create(GrainReadKind? kind, object? /* KLD0041: Accept only the existing runtime-typed GrainValue reply at the final public JSON projection boundary; concrete output validation follows the selected read kind. */ value) => kind switch
    {
        GrainReadKind.Document => (true, new DocumentReadResult(((Require<DocumentResult>(value) /* KLD0041: Require DocumentResult from the existing wire reply before constructing the corresponding concrete public read DTO. */)).Value)),
        GrainReadKind.Message => (true, new MessageInspectionResult(((Require<MessageInspection>(value) /* KLD0041: Require MessageInspection from the existing wire reply before constructing the corresponding concrete public read DTO. */)).Value)),
        GrainReadKind.QueueTransfer => (true, new QueueTransferReadResult(((Require<QueueTransferInspection>(value) /* KLD0041: Require QueueTransferInspection from the existing wire reply before constructing the corresponding concrete public read DTO. */)).Value)),
        GrainReadKind.QueueTransferReceipt => (true, new QueueTransferReceiptReadResult(((Require<QueueTransferReceiptInspection>(value) /* KLD0041: Require QueueTransferReceiptInspection from the existing wire reply before constructing the corresponding concrete public read DTO. */)).Value)),
        GrainReadKind.RecurringSchedule => (true, new RecurringScheduleReadResult(((Require<RecurringScheduleInspection>(value) /* KLD0041: Require RecurringScheduleInspection from the existing wire reply before constructing the corresponding concrete public read DTO. */)).Value)),
        GrainReadKind.Saga => (true, new SagaReadResult(((Require<SagaInspection>(value) /* KLD0041: Require SagaInspection from the existing wire reply before constructing the corresponding concrete public read DTO. */)).Value)),
        _ => (value is not null, value)
    };

    private static (bool HasValue, T? Value) Require<T>(object? /* KLD0041: Accept only the existing runtime-typed GrainValue reply at the final public JSON projection boundary; concrete output validation follows the selected read kind. */ value) where T : class => value is null ? (false, default(T?)) :
        (true, (value as T /* KLD0042: Require the concrete class selected by the read kind from the native reply slot before creating its public DTO; no JSON conversion occurs here. */) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, GrainRoutingProtocol.InvalidRequest));
}
