using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OwnerLossCold
{
    private const string Separator = ":";

    internal static async Task HealthyAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, PartitionMoveRequest move,
        TenantCapacityPlacementRf3OwnerLossHeld.Identity identity, PlacementIntentV1 completed, CancellationToken token)
    {
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, move, token);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, move, before);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await TenantCapacityPlacementRf3OwnerLossReplay.RequireAsync(seed, state, move, identity, completed, token);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, move, token);
        await TenantCapacityPlacementRf3Cold.RequireCanonicalRowsAsync(state, before, after, completed.IntentId, move);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, move, after);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(after);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await TenantCapacityPlacementRf3OwnerLossReplay.RequireAsync(seed, state, move, identity, completed, token);
    }

    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed, PartitionMoveRequest move,
        TenantCapacityPlacementRf3OwnerLossHeld.State held)
    {
        var cuts = TwoRf3MembershipProtocol.Nodes.Select(node =>
            PartitionMovementPublicParentRf3Cut.ReadStopped(wave, node, move, held.Journal.CommandId)).ToArray();
        var key = TenantPlacementPolicyStorage.IntentKey(held.Applying.Plan.TenantId,
            held.Applying.Plan.DatabaseId, held.Applying.IntentId);
        var prefix = Convert.ToHexString(key) + Separator;
        var control = 0;
        foreach (var cut in cuts)
        {
            var row = cut.Rows.SingleOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
            if (row is not null)
            {
                control++;
                var actual = NativeSerialization.Deserialize<PlacementIntentV1>(
                    Convert.FromHexString(row[prefix.Length..]));
                await SqlRf3Protocol.EqualAsync(held.Applying, actual);
            }
            await SqlRf3Protocol.EqualAsync(cut,
                PartitionMovementPublicParentRf3Cut.ReadStopped(wave, cut.Node, move, held.Journal.CommandId));
        }
        await Assert.That(control).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        var observedNode = wave.QueryControls.ArmFor(held.Marker.ArmId).GateVoter
            ?? throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch);
        var original = PhysicalShardPartitionMovementRf3JournalAssertions.ReadPhase(wave, observedNode, held.Journal);
        await Assert.That(original.Error).IsNull();
        var phase = original.Get<PartitionMovePhaseResult>();
        await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.ControlPrepare);
        await Assert.That(phase.MoveId).IsEqualTo(move.MoveId);
        await Assert.That(phase.Control).IsNotNull();
        var actualControl = phase.Control ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
        await Assert.That(actualControl.Phase).IsEqualTo(PartitionMovePhase.Prepared);
        await SqlRf3Protocol.EqualAsync(held.Journal.Journal!, phase.Journal);
        await SqlRf3Protocol.EqualAsync(held.Applying.ControlOwner, phase.Journal.PhysicalOwner);
        await Assert.That(actualControl.Partition).IsEqualTo(move.Partition);
        await Assert.That(actualControl.PrincipalId).IsEqualTo(held.Applying.PrincipalId);
        await Assert.That(actualControl.SourcePlacement.Revision).IsEqualTo(move.ExpectedPlacementRevision);
        await Assert.That(actualControl.SourcePlacement.Incarnation).IsEqualTo(seed.OriginalPlacement.Incarnation);
        var target = seed.Directory.Owners.Single(value => value.Owner.PhysicalShardId
            == move.DestinationPhysicalShardId).Owner;
        await SqlRf3Protocol.EqualAsync(target, actualControl.DestinationOwner);
    }
}
