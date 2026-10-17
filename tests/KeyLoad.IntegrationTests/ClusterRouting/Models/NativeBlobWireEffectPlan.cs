namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record NativeBlobWireEffectPlan(Guid AbortedUploadId, Guid AbortCommandId,
    Guid ReclaimCommandId, Guid DeleteCommandId)
{
    internal static NativeBlobWireEffectPlan Create()
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
}
