namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferRepairReferenceFields
{
    internal const uint Stage = 0;
    internal const uint CapacityGeneration = 1;
    internal const uint PolicyGeneration = 2;
    internal const uint CompleteGeneration = 3;
    internal const uint FailedCommandId = 4;
    internal const uint Fingerprint = 5;
    internal const uint OutcomeDigest = 6;
    internal const uint WitnessToken = 7;
    internal const string Alias = "keyload.core.queue-transfer.repair-reference.v1";
}

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
