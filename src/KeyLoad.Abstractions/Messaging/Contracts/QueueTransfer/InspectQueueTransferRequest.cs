namespace KeyLoad;

/// <summary>Requests authorized inspection of one persisted source transfer.</summary>
/// <param name="SourceQueue">The complete source queue lane.</param>
/// <param name="TransferId">The caller-stable transfer identifier.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(QueueTransferContractAliases.InspectRequest)]
public sealed record InspectQueueTransferRequest(
    [property: global::Orleans.Id(0)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(1)] Guid TransferId);
