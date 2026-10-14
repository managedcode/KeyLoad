using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class GrainPlacementIntentPersistence
{
    internal static async Task<OperationResult> SubmitAsync(IPlacementIntentPersistence persistence,
        ICommitCoordinator coordinator, DecodedGrainRequest original, CancellationToken token)
    {
        var request = GrainNativePayload.ReadPublicInput<PersistPlacementIntentRequest>(original.Payload);
        var operation = await persistence.SealAsync(original.Envelope, request, token).ConfigureAwait(true);
        return await coordinator.SubmitVerifiedAsync(operation, token).ConfigureAwait(true);
    }
}
