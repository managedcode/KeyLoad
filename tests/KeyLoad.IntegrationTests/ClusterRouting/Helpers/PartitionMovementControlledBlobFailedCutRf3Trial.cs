namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementControlledBlobFailedCutRf3Trial
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementControlledBlobCommandRf3Proof proof, CancellationToken token)
    {
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token).ConfigureAwait(false);
        await PartitionMovementControlledBlobLifecycleRf3Calls.RequireAsync(seed, proof, null, token).ConfigureAwait(false);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token).ConfigureAwait(false);
        await PartitionMovementControlledBlobLifecycleRf3Assertions.RequireStoredAsync(seed, [proof], after).ConfigureAwait(false);
        await PartitionMovementControlledBlobFailedCutRf3Assertions.RequireAsync(wave, seed, proof, before, after).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token).ConfigureAwait(false);
    }
}
