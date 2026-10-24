namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferCoordinationReadRequestFields.Alias)]
internal sealed record RemoteTransferCoordinationReadRequest(
    [property: global::Orleans.Id(RemoteTransferCoordinationReadRequestFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadRequestFields.Source)] QueueLaneRef Source,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadRequestFields.Destination)] QueueLaneRef Destination,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadRequestFields.TransferId)] Guid TransferId,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadRequestFields.IntentToken)] string IntentToken,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadRequestFields.ExpectedGeneration)] long ExpectedGeneration,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadRequestFields.AcceptCommandId)] Guid AcceptCommandId,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadRequestFields.RepairStage)] QueueTransferRepairStage? RepairStage = null,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadRequestFields.PolicyGeneration)] long PolicyGeneration = RemoteTransferRepairProtocol.InitialGeneration,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadRequestFields.CompleteGeneration)] long CompleteGeneration = RemoteTransferRepairProtocol.InitialGeneration,
    [property: global::Orleans.Id(RemoteTransferCoordinationReadRequestFields.ReceiptToken)] string? ReceiptToken = null);
