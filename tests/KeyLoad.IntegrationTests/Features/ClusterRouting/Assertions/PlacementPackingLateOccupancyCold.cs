using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class PlacementPackingLateOccupancyCold(
    TenantCapacityPlacementRf3PendingPolicyOperation.Original original,
    PlacementIntentV1 applying, TenantPlacementPolicyV1 policy)
{
    private TenantCapacityPlacementRf3PendingPolicyRows.Rows? first;

    internal async Task RequireAsync(PartitionMovementPublicParentRf3NativeCut[] cuts)
    {
        var actual = await TenantCapacityPlacementRf3PendingPolicyRows.RequireAsync(cuts, original,
            applying, policy, first?.Persist);
        if (first is not null)
        {
            await SqlRf3Protocol.EqualAsync(first, actual);
        }
        first = actual;
    }
}
