using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMinimumRf3Cold
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, ChunkMinimumRf3State state, CancellationToken token)
    {
        _ = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, state.Move, token);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await ChunkMinimumRf3Flow.CurrentAsync(wave, state, token);
    }
}
