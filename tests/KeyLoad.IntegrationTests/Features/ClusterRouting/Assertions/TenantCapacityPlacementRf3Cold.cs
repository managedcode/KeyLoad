using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Cold
{
    internal static async Task ReopenAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await seed.ReconnectAsync(wave, token);
        await seed.VerifyAsync(token);
    }

    internal static async Task RequireDirtyReplayAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        PartitionMovementPublicParentRf3NativeCut[] original, CancellationToken token)
    {
        foreach (var owner in original)
        {
            await Assert.That(owner.Header).IsNull();
            await Assert.That(owner.Pending).IsNull();
            await Assert.That(owner.LastIssued).IsNull();
        }
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(original);
        await RequireFullColdOpenAsync(wave, seed.FirstRequest, original);
        await ReopenAsync(wave, seed, token);
        await TenantCapacityPlacementRf3Assertions.RequirePolicyReplayAsync(seed, state, token);
        var replay = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await RequireCanonicalRowsAsync(state, original, replay, null, seed.FirstRequest);
        await RequireFullColdOpenAsync(wave, seed.FirstRequest, replay);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(replay);
        await ReopenAsync(wave, seed, token);
    }

    internal static async Task RequireHealthyReplayAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        PartitionMoveRequest move, Guid id, ApplyPlacementIntentRequest request,
        PlacementIntentV1 original, CancellationToken token)
    {
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, move, token);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before);
        await RequireFullColdOpenAsync(wave, move, before);
        await ReopenAsync(wave, seed, token);
        await TenantCapacityPlacementRf3Apply.RequireReplayAsync(seed, state, id, request, original, token);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, move, token);
        await RequireCanonicalRowsAsync(state, before, after, original.IntentId, move);
        await RequireFullColdOpenAsync(wave, move, after);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(after);
        await ReopenAsync(wave, seed, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
    }
    internal static async Task RequireRefusalCutAsync(TenantCapacityPlacementRf3Provisioning.State state,
        PartitionMovementPublicParentRf3NativeCut[] original, PartitionMovementPublicParentRf3NativeCut[] stopped,
        PartitionMoveRequest move)
    {
        await Assert.That(stopped.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        foreach (var owner in stopped)
        {
            await Assert.That(owner.Header).IsNull();
            await Assert.That(owner.Pending).IsNull();
            await Assert.That(owner.LastIssued).IsNull();
        }
        await RequireCanonicalRowsAsync(state, original, stopped, null, move);
    }

    private static async Task RequireFullColdOpenAsync(TwoRf3MembershipWave wave, PartitionMoveRequest move,
        PartitionMovementPublicParentRf3NativeCut[] original)
    {
        foreach (var cut in original)
        {
            var reopened = PartitionMovementPublicParentRf3Cut.ReadStopped(wave, cut.Node, move, Guid.Empty);
            await SqlRf3Protocol.EqualAsync(cut, reopened);
        }
    }

    private static async Task RequireCanonicalRowsAsync(TenantCapacityPlacementRf3Provisioning.State state,
        PartitionMovementPublicParentRf3NativeCut[] original, PartitionMovementPublicParentRf3NativeCut[] after,
        Guid? intentId, PartitionMoveRequest move)
    {
        const int ControlVoters = 3;
        var policy = TenantPlacementPolicyStorage.PolicyKey(state.Control.TenantId, state.Control.DatabaseId);
        var outcome = CommandOutcomeKeyResolver.ForNew(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            state.OriginalPolicyCommand, new(CommandOutcomeScopeKind.Partition, state.Control)).Key;
        var keys = new List<byte[]> { policy, outcome };
        if (intentId is { } intent)
        {
            keys.Add(TenantPlacementPolicyStorage.IntentKey(state.Control.TenantId, state.Control.DatabaseId, intent));
            keys.Add(PartitionMoveParentKeys.Header(move.Partition, move.MoveId));
        }
        await Assert.That(original.Count(cut => Row(cut, policy) is not null)).IsEqualTo(ControlVoters);
        foreach (var before in original)
        {
            var current = after.Single(cut => cut.Node == before.Node);
            await Assert.That(current.StorePosition).IsGreaterThanOrEqualTo(before.StorePosition);
            foreach (var key in keys)
            { await Assert.That(Row(current, key)).IsEqualTo(Row(before, key)); }
            await Assert.That(current.OutstandingMoveGrants).IsEqualTo(before.OutstandingMoveGrants);
            await Assert.That(current.OutstandingDatabaseGrants).IsEqualTo(before.OutstandingDatabaseGrants);
        }
    }

    private static string? Row(PartitionMovementPublicParentRf3NativeCut cut, byte[] key)
    {
        var prefix = Convert.ToHexString(key) + ":";
        return cut.Rows.SingleOrDefault(row => row.StartsWith(prefix, StringComparison.Ordinal));
    }
}
