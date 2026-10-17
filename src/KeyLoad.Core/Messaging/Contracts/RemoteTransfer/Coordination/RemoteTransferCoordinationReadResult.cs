namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferCoordinationReadResultFields.Alias)]
internal sealed record RemoteTransferCoordinationReadResult(
    [property: global::Orleans.Id(RemoteTransferCoordinationReadResultFields.SourceState)] RemoteTransferCoordinationHint? SourceState,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadResultFields.FailureWitness)] string? FailureWitness,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadResultFields.TargetReceipt)] QueueTransferReceiptInspection? TargetReceipt,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadResultFields.RepairWitness)] string? RepairWitness = null);
