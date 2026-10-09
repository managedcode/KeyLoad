using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual B observations are independently joined to original native entries and stopped outcomes.</summary>
internal static class MovementFrameObservationRf3Assertions
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMovementFinalInstallFramePrecut precut,
        PartitionMovementPublicParentRf3NativeCut[] after, int legalFrameBytes)
    {
        var fixture = wave.frameObservation
            ?? throw new InvalidOperationException(MovementFrameObservationFixtureProtocol.Invalid);
        var observations = fixture.ReadAll();
        await Assert.That(observations.Count).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var pair in observations)
        {
            var before = precut.Cuts.Single(cut => cut.Node == pair.Key);
            var current = after.Single(cut => cut.Node == pair.Key);
            var issued = current.OriginalReceiverIssuance
                ?? throw new InvalidOperationException(MovementFrameObservationFixtureProtocol.Invalid);
            var entries = PartitionMovementCapturePointerRf3Fault.AppliedEntries(wave, before, current);
            var original = entries.Single(entry => entry.Operation?.Id == precut.EffectId);
            var voter = issued.ReceiverOwner.VoterIds.Single(value => new Uri(value, UriKind.Absolute).Host == pair.Key);
            await Assert.That(pair.Value.Voter).IsEqualTo(voter);
            await RequireIdentityAsync(seed, fixture, pair.Value, issued, original, precut);
            await Assert.That(pair.Value.RejectedPrefixBytes).IsEqualTo((long)legalFrameBytes);
            await Assert.That(pair.Value.MaximumFrameBytes).IsEqualTo(checked(legalFrameBytes
                - MovementFrameObservationProtocol.PrefixStep));
            await Assert.That(ZoneTreeStore.IsEncodedFrameLimitRejection(current.OriginalNativeResult)).IsTrue();
            await Assert.That(PartitionMovementCapturePointerRf3Fault.Applied(current))
                .IsGreaterThanOrEqualTo(original.Index);
        }
    }

    private static async Task RequireIdentityAsync(PartitionMovementPublicParentRf3Seed seed,
        MovementFrameObservationFixture fixture, MovementFrameObservationRecord observed,
        PartitionMoveReceiverIssuance issued, ReplicaEntry entry, PartitionMovementFinalInstallFramePrecut precut)
    {
        var original = entry.Operation
            ?? throw new InvalidOperationException(MovementFrameObservationFixtureProtocol.Invalid);
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(original);
        var admission = phase.ReceiverEffectAdmission
            ?? throw new InvalidOperationException(MovementFrameObservationFixtureProtocol.Invalid);
        await Assert.That(observed.SessionId).IsEqualTo(fixture.SessionId);
        await Assert.That(observed.SelectionId).IsEqualTo(fixture.SelectionId);
        await Assert.That(observed.MoveId).IsEqualTo(seed.FirstRequest.MoveId);
        await Assert.That(observed.Partition.ToPartition()).IsEqualTo(seed.Partition);
        await Assert.That(observed.OperatorPrincipalId).IsEqualTo(issued.SourceOperatorPrincipalId);
        await Assert.That(observed.ReceiverPrincipalId).IsEqualTo(original.PrincipalId);
        await Assert.That(observed.ReceiverPrincipalId).IsEqualTo(issued.ReceiverPrincipalId);
        await Assert.That(issued.ReceiverOwner.VoterIds.Contains(observed.Voter, StringComparer.Ordinal)).IsTrue();
        await Assert.That(observed.PhysicalShardId).IsEqualTo(issued.ReceiverOwner.PhysicalShardId);
        await Assert.That(observed.Incarnation).IsEqualTo(issued.ReceiverOwner.Incarnation);
        await Assert.That(observed.EffectCommandId).IsEqualTo(precut.EffectId);
        await Assert.That(observed.EntryIndex).IsEqualTo(entry.Index);
        await Assert.That(observed.EntryTerm).IsEqualTo(entry.Term);
        await Assert.That(observed.OriginalRequestNonce).IsEqualTo(issued.OriginalRequestNonce);
        await Assert.That(observed.OriginalRequestNonce).IsEqualTo(admission.OriginalRequestNonce);
        await Assert.That(observed.OriginalExpiresAt).IsEqualTo(issued.OriginalExpiresAt);
        await Assert.That(observed.OriginalExpiresAt).IsEqualTo(admission.OriginalExpiresAt);
        await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.Install);
        await Assert.That(phase.PageOrdinal).IsEqualTo(precut.FinalOrdinal);
    }
}
