namespace KeyLoad;

/// <summary>Names the immutable Orleans wire and persisted-contract aliases for queue transfers.</summary>
internal static class QueueTransferContractAliases
{
    internal const string Create = "keyload.contract.create-queue-transfer.v1";
    internal const string Accept = "keyload.contract.accept-queue-transfer.v1";
    internal const string Complete = "keyload.contract.complete-queue-transfer.v1";
    internal const string InspectRequest = "keyload.contract.inspect-queue-transfer-request.v1";
    internal const string Inspection = "keyload.contract.queue-transfer-inspection.v1";
    internal const string ReceiptInspectionRequest = "keyload.contract.inspect-queue-transfer-receipt-request.v1";
    internal const string ReceiptInspection = "keyload.contract.queue-transfer-receipt-inspection.v1";
    internal const string State = "keyload.contract.queue-transfer-state.v1";
}

/// <summary>Identifies a durable cross-partition queue transfer lifecycle.</summary>
public enum QueueTransferState
{
    /// <summary>The source intent is committed and awaits a destination receipt.</summary>
    OutputPending,
    /// <summary>The source committed a valid receipt from the destination partition.</summary>
    Delivered
}

/// <summary>Persists an immutable source intent for one stable transfer identity.</summary>
/// <param name="SourceQueue">The complete source queue lane.</param>
/// <param name="TransferId">The caller-stable transfer identifier.</param>
/// <param name="Destination">The complete destination queue lane.</param>
/// <param name="Message">The exact message to enqueue at the destination.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(QueueTransferContractAliases.Create)]
public sealed record CreateQueueTransfer(
    [property: global::Orleans.Id(0)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(1)] Guid TransferId,
    [property: global::Orleans.Id(2)] QueueLaneRef Destination,
    [property: global::Orleans.Id(3)] EnqueueMessage Message) : Mutation(SourceQueue.Queue);

/// <summary>Accepts a signed source intent on its destination partition.</summary>
/// <param name="DestinationQueue">The complete destination queue lane.</param>
/// <param name="IntentToken">The signed immutable source intent.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(QueueTransferContractAliases.Accept)]
public sealed record AcceptQueueTransfer(
    [property: global::Orleans.Id(0)] QueueLaneRef DestinationQueue,
    [property: global::Orleans.Id(1)] string IntentToken) : Mutation(DestinationQueue.Queue);

/// <summary>Completes a pending source intent using a signed destination receipt.</summary>
/// <param name="SourceQueue">The complete source queue lane.</param>
/// <param name="TransferId">The caller-stable transfer identifier.</param>
/// <param name="ReceiptToken">The signed committed destination receipt.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(QueueTransferContractAliases.Complete)]
public sealed record CompleteQueueTransfer(
    [property: global::Orleans.Id(0)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(1)] Guid TransferId,
    [property: global::Orleans.Id(2)] string ReceiptToken) : Mutation(SourceQueue.Queue);

/// <summary>Requests authorized inspection of one persisted source transfer.</summary>
/// <param name="SourceQueue">The complete source queue lane.</param>
/// <param name="TransferId">The caller-stable transfer identifier.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(QueueTransferContractAliases.InspectRequest)]
public sealed record InspectQueueTransferRequest(
    [property: global::Orleans.Id(0)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(1)] Guid TransferId);

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
