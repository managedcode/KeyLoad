using KeyLoad.Server.Features.BlobStorage;

namespace KeyLoad.Server.Features.DocumentStorage;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteDocumentProtocol.EnvelopeAlias)]
internal sealed record RemoteDocumentTransportEnvelope(
    [property: global::Orleans.Id(0)] RemoteDocumentCallV1? Document,
    [property: global::Orleans.Id(1)] RemoteControlledDocumentCall? Controlled,
    [property: global::Orleans.Id(2)] RemoteControlledBlobCall? ControlledBlob = null,
    [property: global::Orleans.Id(3)] global::KeyLoad.Core.Features.Messaging.RemoteQueueTransferPeerCall? QueueTransfer = null);
