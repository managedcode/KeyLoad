namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeReplayCatalog
{
    private const int OriginalReplayCount = 2;
    private const int SingleFaultReplayCount = 1;
    private const int FirstWitnessOrdinal = 1;
    private static readonly RemoteModelNativeReplayVariation[] WireFaults =
    [
        RemoteModelNativeReplayVariation.MissingQueueTransfer, RemoteModelNativeReplayVariation.ReceiptStage,
        RemoteModelNativeReplayVariation.MissingIntentToken, RemoteModelNativeReplayVariation.EmptyNonce,
        RemoteModelNativeReplayVariation.EmptyCommandId, RemoteModelNativeReplayVariation.EmptyDestinationIncarnation
    ];

    internal static bool IsIndividual(RemoteModelNativeReplayVariation mode) => mode is
        RemoteModelNativeReplayVariation.Original2 or RemoteModelNativeReplayVariation.MissingQueueTransfer
        or RemoteModelNativeReplayVariation.ReceiptStage or RemoteModelNativeReplayVariation.MissingIntentToken
        or RemoteModelNativeReplayVariation.EmptyNonce or RemoteModelNativeReplayVariation.EmptyCommandId
        or RemoteModelNativeReplayVariation.EmptyDestinationIncarnation;

    internal static int Count(RemoteModelNativeReplayVariation planMode) => planMode switch
    {
        RemoteModelNativeReplayVariation.Original2 => OriginalReplayCount,
        RemoteModelNativeReplayVariation.AllWireFaults6 => WireFaults.Length,
        _ when IsIndividual(planMode) => SingleFaultReplayCount,
        _ => throw Invalid()
    };

    internal static RemoteModelNativeReplayVariation At(RemoteModelNativeReplayVariation planMode, int ordinal)
    {
        if (ordinal < FirstWitnessOrdinal || ordinal > Count(planMode)) { throw Invalid(); }
        return planMode == RemoteModelNativeReplayVariation.AllWireFaults6 ? WireFaults[ordinal - FirstWitnessOrdinal] : planMode;
    }

    // A decoded individual witness may belong to AllWireFaults6; its plan closes the exact ordinal mapping.
    internal static int MaximumWitnessOrdinal(RemoteModelNativeReplayVariation individual)
    {
        if (!IsIndividual(individual)) { throw Invalid(); }
        return individual == RemoteModelNativeReplayVariation.Original2 ? OriginalReplayCount : WireFaults.Length;
    }

    private static InvalidOperationException Invalid() => new(RemoteModelNativeProtocol.InvalidProof);
}
