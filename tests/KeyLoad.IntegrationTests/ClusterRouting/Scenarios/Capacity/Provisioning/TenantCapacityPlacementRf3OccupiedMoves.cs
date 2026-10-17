using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OccupiedMoves
{
    internal static async Task<PartitionMoveResult> RunAsync(TwoRf3MembershipWave wave,
        PartitionMoveRequest request, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            wave.Profile.AdminKey, async root =>
            {
                var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.MovePartitionAsync(request, token));
                await Assert.That(receipt.MoveId).IsEqualTo(request.MoveId);
                await Assert.That(receipt.Partition).IsEqualTo(request.Partition);
                await Assert.That(receipt.Phase).IsEqualTo(PartitionMovePhase.Retired);
                await PartitionMovementPublicCallerReplay.RequireAsync(root.Sdk, root.Mcp, request, receipt, token);
                return receipt;
            }, token);

    internal static async Task ReplayAsync(TwoRf3MembershipWave wave, PartitionMoveRequest request,
        PartitionMoveResult original, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            wave.Profile.AdminKey, async root =>
            {
                await PartitionMovementPublicCallerReplay.RequireAsync(root.Sdk, root.Mcp, request, original, token);
                return original;
            }, token);
    }
}
