namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPlacementHysteresisExclusiveRf3Cold
{
    private const int First = 0;
    private const int ColdCuts = 2;

    internal static async Task RequireAsync(TenantPlacementHysteresisExclusiveRf3State state,
        PartitionMoveRequest request, PartitionMoveResult original, CancellationToken token)
    {
        PartitionMovementPublicParentRf3NativeCut[]? before = null;
        for (var index = First; index < ColdCuts; index++)
        {
            var cut = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(state.Wave, request, token);
            await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cut);
            await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(state.Wave, request, cut);
            if (before is not null)
            {
                await TenantCapacityPlacementRf3OccupiedRows.RequireAsync(state.Capacity, state.Packed,
                    before, cut, request, token);
            }
            before = cut;
            await PartitionMovementPublicParentRf3Cut.RestartAsync(state.Wave, token);
            await state.Seed.ReconnectAsync(state.Wave, token);
            await TenantPlacementHysteresisExclusiveRf3Current.RequireAsync(state, token);
            await TenantPlacementHysteresisExclusiveRf3Moves.ReplayAsync(state, request, original, token);
            await TenantPlacementHysteresisExclusiveRf3Policy.ConfigureAsync(state, token);
            await TenantPlacementHysteresisExclusiveRf3Plans.RequireAsync(state, token);
        }
    }
}
