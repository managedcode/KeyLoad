using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class ControlledBlobOutcomeLifetimeNativeCold
{
    private const int ColdCycles = 2;

    internal static async Task ExecuteAsync(ControlledBlobOutcomeLifetimeNativeState state, CancellationToken token)
    {
        foreach (var proof in state.Proofs)
        {
            await ControlledBlobOutcomeLifetimeNativeReads.HealthyAsync(state, proof, token);
        }
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        for (var pass = 0; pass < ColdCycles; pass++)
        {
            state.Source.Reopen();
            state.Target.Reopen();
            await ControlledBlobOutcomeLifetimeNativeAssertions.UnchangedAsync(state, source, target);
            foreach (var proof in state.Proofs)
            {
                await ControlledBlobOutcomeLifetimeNativeReads.HealthyAsync(state, proof, token);
            }
            await ControlledBlobOutcomeLifetimeNativeAssertions.UnchangedAsync(state, source, target);
        }
    }
}
