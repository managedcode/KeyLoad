using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3MoveReplay
{
    internal static async Task VerifyAsync(TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        ResourcePartitionInventoryRf3MoveState state, CancellationToken token)
    {
        using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token);
        var before = await ResourcePartitionInventoryRf3MoveInventory.CaptureAsync(
            wave, seed, membership, state.Before, state.Terminal, token);
        await ResourcePartitionInventoryRf3Assertions.SameMembershipAsync(state.After, before);
        await ResourcePartitionInventoryRf3PublicAssertions.ReplayAsync(wave, state.Originals, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, seed.Credential,
            async callers =>
            {
                await PartitionMovementPublicCallerReplay.RequireAsync(callers.Sdk, callers.Mcp,
                    state.Request, state.Terminal, token);
                return true;
            }, token);
        var after = await ResourcePartitionInventoryRf3MoveInventory.CaptureAsync(
            wave, seed, membership, state.Before, state.Terminal, token);
        await ResourcePartitionInventoryPolicyRefusal.UnchangedAsync(before.First, after.First);
        await ResourcePartitionInventoryPolicyRefusal.UnchangedAsync(before.Second, after.Second);
        await ResourcePartitionInventoryPolicyRefusal.UnchangedAsync(before.Unused, after.Unused);
    }
}
