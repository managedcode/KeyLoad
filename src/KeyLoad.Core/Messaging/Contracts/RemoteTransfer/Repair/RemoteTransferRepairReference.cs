namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferRepairReferenceFields.Alias)]
internal sealed record RemoteTransferRepairReference(
    [property: global::Orleans.Id(RemoteTransferRepairReferenceFields.Stage)] QueueTransferRepairStage Stage,
    [property: global::Orleans.Id(RemoteTransferRepairReferenceFields.CapacityGeneration)] long CapacityGeneration,
    [property: global::Orleans.Id(RemoteTransferRepairReferenceFields.PolicyGeneration)] long PolicyGeneration,
    [property: global::Orleans.Id(RemoteTransferRepairReferenceFields.CompleteGeneration)] long CompleteGeneration,
    [property: global::Orleans.Id(RemoteTransferRepairReferenceFields.FailedCommandId)] Guid FailedCommandId,
    [property: global::Orleans.Id(RemoteTransferRepairReferenceFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(RemoteTransferRepairReferenceFields.OutcomeDigest)] string OutcomeDigest,
    [property: global::Orleans.Id(RemoteTransferRepairReferenceFields.WitnessToken)] string WitnessToken);
