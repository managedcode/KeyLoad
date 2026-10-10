namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferPeerProtocol.ResultAlias)]
internal sealed record RemoteQueueTransferPeerResult(
    [property: global::Orleans.Id(RemoteTransferResultFields.Stage)] RemoteQueueTransferPeerStage Stage,
    [property: global::Orleans.Id(RemoteTransferResultFields.Receipt)] QueueTransferReceiptInspection? Receipt,
    [property: global::Orleans.Id(RemoteTransferResultFields.OriginalOutcome)] StoredOutcome? OriginalOutcome);
