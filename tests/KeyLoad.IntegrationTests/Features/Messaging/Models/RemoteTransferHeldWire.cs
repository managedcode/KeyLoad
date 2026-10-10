using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record RemoteTransferHeldWire(RemoteDocumentTransportEnvelope Original, byte[] Bytes,
    string Signature, int Receiver);
