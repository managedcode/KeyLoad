namespace KeyLoad;

/// <summary>Requests authorized lookup of one destination's immutable dedup receipt.</summary>
/// <param name="DestinationQueue">The complete destination queue lane.</param>
/// <param name="SourceQueue">The complete source queue lane.</param>
/// <param name="TransferId">The caller-stable transfer identifier.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(QueueTransferContractAliases.ReceiptInspectionRequest)]
public sealed record InspectQueueTransferReceiptRequest(
    [property: global::Orleans.Id(0)] QueueLaneRef DestinationQueue,
    [property: global::Orleans.Id(1)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(2)] Guid TransferId);
