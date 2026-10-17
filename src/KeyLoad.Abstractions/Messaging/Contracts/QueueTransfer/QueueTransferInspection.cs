namespace KeyLoad;

/// <summary>Returns persisted transfer state and its authenticated protocol tokens.</summary>
/// <param name="SourceQueue">The complete source queue lane.</param>
/// <param name="TransferId">The caller-stable transfer identifier.</param>
/// <param name="Destination">The complete destination queue lane.</param>
/// <param name="State">The persisted source lifecycle state.</param>
/// <param name="IntentToken">The signed source intent.</param>
/// <param name="ReceiptToken">The signed destination receipt, when completed.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(QueueTransferContractAliases.Inspection)]
public sealed record QueueTransferInspection(
    [property: global::Orleans.Id(0)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(1)] Guid TransferId,
    [property: global::Orleans.Id(2)] QueueLaneRef Destination,
    [property: global::Orleans.Id(3)] QueueTransferState State,
    [property: global::Orleans.Id(4)] string IntentToken,
    [property: global::Orleans.Id(5)] string? ReceiptToken);
