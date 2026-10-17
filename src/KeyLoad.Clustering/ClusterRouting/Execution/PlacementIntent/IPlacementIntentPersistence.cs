
namespace KeyLoad.Clustering;

internal interface IPlacementIntentPersistence
{
    Task<ReplicatedOperation> SealAsync(GrainRequestEnvelope trusted, PersistPlacementIntentRequest request,
        CancellationToken token);
}
