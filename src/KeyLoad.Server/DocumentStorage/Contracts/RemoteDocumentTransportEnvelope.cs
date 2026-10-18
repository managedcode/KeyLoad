namespace KeyLoad.Server.Features.DocumentStorage;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteDocumentProtocol.EnvelopeAlias)]
internal sealed record RemoteDocumentTransportEnvelope(
    [property: global::Orleans.Id(0)] RemoteDocumentCallV1? Document,
    [property: global::Orleans.Id(3)] global::KeyLoad.Core.Features.Messaging.RemoteQueueTransferPeerCall? QueueTransfer = null);
