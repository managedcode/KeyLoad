namespace KeyLoad;

/// <summary>Returns the committed destination proof without claiming source completion.</summary>
/// <param name="DestinationQueue">The complete destination queue lane.</param>
/// <param name="SourceQueue">The complete source queue lane.</param>
/// <param name="TransferId">The caller-stable transfer identifier.</param>
/// <param name="ReceiptToken">The signed immutable destination receipt.</param>
/// <param name="TargetCommit">The committed destination cut bound by the receipt.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(QueueTransferContractAliases.ReceiptInspection)]
public sealed record QueueTransferReceiptInspection(
    [property: global::Orleans.Id(0)] QueueLaneRef DestinationQueue,
    [property: global::Orleans.Id(1)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(2)] Guid TransferId,
    [property: global::Orleans.Id(3)] string ReceiptToken,
    [property: global::Orleans.Id(4)] CommitToken TargetCommit);
