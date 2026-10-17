namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferCoordinationReadRequestFields
{
    internal const uint Purpose = 0;
    internal const uint Source = 1;
    internal const uint Destination = 2;
    internal const uint TransferId = 3;
    internal const uint IntentToken = 4;
    internal const uint ExpectedGeneration = 5;
    internal const uint AcceptCommandId = 6;
    internal const uint RepairStage = 7;
    internal const uint PolicyGeneration = 8;
    internal const uint CompleteGeneration = 9;
    internal const uint ReceiptToken = 10;
    internal const string Alias = "keyload.core.queue-transfer.coordination-read.v1";
}

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
