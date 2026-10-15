namespace KeyLoad.Client;

/// <summary>Payload-free local identity evidence retained if a fatal invocation cannot return a result.</summary>
/// <param name="OperationId">Actual original receive or delivery GUID.</param>
/// <param name="Action">Actual renewal or ACK action; null for receive.</param>
/// <param name="Stage">Actual original observation stage.</param>
public sealed record QueueWorkerOperationIdentity(Guid OperationId, DeliveryAction? Action, QueueWorkerOperationStage Stage);
