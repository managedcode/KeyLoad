namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferCoordinationReadResultFields
{
    internal const uint SourceState = 0;
    internal const uint FailureWitness = 1;
    internal const uint TargetReceipt = 2;
    internal const uint RepairWitness = 3;
    internal const string Alias = "keyload.core.queue-transfer.coordination-read-result.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferCoordinationReadResultFields.Alias)]
internal sealed record RemoteTransferCoordinationReadResult(
    [property: global::Orleans.Id(RemoteTransferCoordinationReadResultFields.SourceState)] RemoteTransferCoordinationHint? SourceState,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadResultFields.FailureWitness)] string? FailureWitness,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadResultFields.TargetReceipt)] QueueTransferReceiptInspection? TargetReceipt,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadResultFields.RepairWitness)] string? RepairWitness = null);
