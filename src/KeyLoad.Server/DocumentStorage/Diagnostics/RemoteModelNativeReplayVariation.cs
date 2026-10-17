namespace KeyLoad.Server.Features.DocumentStorage;

internal enum RemoteModelNativeReplayVariation
{
    Original2,
    MissingQueueTransfer,
    ReceiptStage,
    MissingIntentToken,
    EmptyNonce,
    EmptyCommandId,
    EmptyDestinationIncarnation,
    AllWireFaults6 = 7
}
