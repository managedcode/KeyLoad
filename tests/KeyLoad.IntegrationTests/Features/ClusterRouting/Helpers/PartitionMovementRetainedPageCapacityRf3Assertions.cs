
namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>The extra real blob and every original parent model survive whole terminal/cold continuation.</summary>
internal static class PartitionMovementRetainedPageCapacityRf3Assertions
{
    internal static async Task RequireHealthyBlobAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMovementRetainedPageCapacityRf3BlobCorpus blob,
        CancellationToken cancellationToken)
    {
        await blob.RequireAsync(seed, cancellationToken);
        await seed.VerifyAsync(cancellationToken);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        await blob.RequireAsync(seed, cancellationToken);
        await seed.VerifyAsync(cancellationToken);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
    }
}
