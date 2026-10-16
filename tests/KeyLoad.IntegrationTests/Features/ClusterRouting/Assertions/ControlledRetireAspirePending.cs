using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.ClusterRouting.ControlledRetireAspireProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ControlledRetireAspirePending
{
    internal static async Task<ControlledRetireAspireOriginal> RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        IReadOnlyDictionary<string, NativeRetireCutWitness> cuts, long generation)
    {
        var captured = cuts.Single(pair => pair.Value.OriginalSealedOperation is not null);
        var raw = captured.Value.OriginalSealedOperation!.Value.ToArray();
        var original = NativeSerialization.Deserialize<ReplicatedOperation>(raw);
        var phase = global::KeyLoad.Core.Features.ClusterRouting.Serialization.PartitionMovementNativePayload.Read(original);
        var admission = phase.ReceiverEffectAdmission ?? throw new InvalidOperationException(P.Missing);
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(phase.Body.Span);
        var publication = body.Publication ?? throw new InvalidOperationException(P.Missing);
        await Assert.That(original.Kind).IsEqualTo(OperationKind.PartitionMovementPhase);
        await Assert.That(original.PrincipalId).IsEqualTo(PartitionMovementPublicParentRf3Administrator.PrincipalId);
        await Assert.That(original.Id).IsEqualTo(PartitionMovementParentPhaseIds.For(seed.FirstRequest,
            original.PrincipalId, PartitionMovementParentPhaseRole.Retire, cleanupGeneration: generation));
        await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.Retire);
        await Assert.That(body.Role).IsEqualTo(PartitionMoveCleanupRole.Source);
        await Assert.That(body.FamilyOrdinal).IsEqualTo(P.First);
        await Assert.That(body.Control.Phase).IsEqualTo(PartitionMovePhase.Published);
        await Assert.That(body.Control.SourceCut).IsGreaterThan(P.InitialCleanup);
        var target = seed.Directory.Owners.Single(owner => owner.Owner.PhysicalShardId
            == seed.FirstRequest.DestinationPhysicalShardId).Owner;
        await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementV1(P.Version, seed.Partition,
            target.PhysicalShardId, seed.FirstRequest.ExpectedPlacementRevision + P.Step, target.Incarnation,
            target.VoterIds, phase.SourcePlacement.PlacementEpoch + P.Step), publication.Placement);
        var key = PartitionMoveParentKeys.Phase(seed.Partition, seed.FirstRequest.MoveId, original.Id);
        var bytes = captured.Value.Rows.Single(row => row.Key.Span.SequenceEqual(key)).Value.ToArray();
        var retained = NativeSerialization.Deserialize<PartitionMoveParentPhase>(bytes);
        await PendingAsync(retained, original.Id, generation, admission);
        foreach (var cut in cuts.Values)
        {
            var outcome = KeySpace.PartitionOutcome(seed.Partition, original.PrincipalId, original.Id);
            await Assert.That(cut.Rows.Any(row => row.Key.Span.SequenceEqual(outcome))).IsFalse();
        }
        return new(captured.Key, original, raw, retained, bytes, new(seed.FirstRequest.MoveId, seed.Partition,
            PartitionMovePhase.Retired, seed.Directory.ControlOwner, target, body.Control.SourceCut,
            publication.Installed, publication.Placement));
    }

    private static async Task PendingAsync(PartitionMoveParentPhase retained, Guid originalId,
        long generation, PartitionMoveReceiverEffectAdmission admission)
    {
        await Assert.That(retained.OriginalPhaseCommandId).IsEqualTo(originalId);
        await Assert.That(retained.Stage).IsEqualTo(PartitionMovePeerStage.Retire);
        await Assert.That(retained.CleanupGeneration).IsEqualTo(generation);
        await Assert.That(retained.OriginalRequestNonce).IsEqualTo(admission.OriginalRequestNonce);
        await Assert.That(retained.OriginalExpiresAt).IsEqualTo(admission.OriginalExpiresAt);
        await Assert.That(retained.OriginalResult).IsNull();
        await Assert.That(retained.ObservationCheckpointReceipt).IsNull();
        await Assert.That(retained.OriginalAuthorization).IsNotNull();
        await Assert.That(retained.OriginalGrant).IsNotNull();
        await Assert.That(retained.OriginalReceiverIssuanceWitness).IsNotNull();
        await Assert.That(retained.OriginalReceiverSourceWitness).IsNotNull();
        await Assert.That(retained.OriginalReceiverIssuePacket).IsNotNull();
    }

    internal static async Task RetainedAsync(ControlledRetireAspireOriginal original,
        PartitionMovementPublicParentRf3NativeCut[] cuts)
    {
        var actual = cuts.Single(cut => cut.Node == original.Node);
        var key = PartitionMoveParentKeys.Phase(original.Phase.Partition, original.Phase.MoveId, original.Operation.Id);
        var prefix = Convert.ToHexString(key) + ":";
        await Assert.That(actual.Rows.Single(row => row.StartsWith(prefix, StringComparison.Ordinal)))
            .IsEqualTo(prefix + Convert.ToHexString(original.PhaseBytes));
        await Assert.That(actual.OriginalNativeResult).IsNull();
        await Assert.That(actual.OriginalPhase!.OriginalResult).IsNull();
        await Assert.That(actual.OriginalPhase.ObservationCheckpointReceipt).IsNull();
        var principalPrefix = Convert.ToHexString(KeySpace.Principal(original.Operation.PrincipalId)) + ":";
        var principalRow = actual.Rows.Single(row => row.StartsWith(principalPrefix, StringComparison.Ordinal));
        await PrincipalAsync(original, Convert.FromHexString(principalRow[principalPrefix.Length..]));
    }

    internal static async Task RetainedAsync(ControlledRetireAspireOriginal original,
        IReadOnlyDictionary<string, NativeRetireCutWitness> cuts)
    {
        await Assert.That(cuts.Count).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        var key = PartitionMoveParentKeys.Phase(original.Phase.Partition, original.Phase.MoveId, original.Operation.Id);
        var actual = cuts[original.Node].Rows.Single(row => row.Key.Span.SequenceEqual(key));
        await Assert.That(actual.Value.Span.SequenceEqual(original.PhaseBytes)).IsTrue();
        var principalKey = KeySpace.Principal(original.Operation.PrincipalId);
        await PrincipalAsync(original, cuts[original.Node].Rows.Single(row => row.Key.Span.SequenceEqual(principalKey)).Value);
        var outcome = KeySpace.PartitionOutcome(original.Phase.Partition, original.Operation.PrincipalId, original.Operation.Id);
        foreach (var cut in cuts.Values)
        {
            await Assert.That(cut.Rows.Any(row => row.Key.Span.SequenceEqual(outcome))).IsFalse();
        }
    }

    internal static async Task InstallAsync(PartitionMovementPublicParentRf3Seed seed,
        ControlledRetireAspireOriginal original, IReadOnlyDictionary<string, NativeRetireCutWitness> cuts)
    {
        var token = original.Expected.InstalledReceipt ?? throw new InvalidOperationException(P.Missing);
        var target = original.Expected.DestinationOwner;
        var targets = cuts.Values.Where(cut => cut.Owner.Incarnation == target.Incarnation).ToArray();
        await Assert.That(targets.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var cut in cuts.Values.Where(cut => cut.Owner.Incarnation != target.Incarnation))
        {
            await Assert.That(cut.ReceiptEntry).IsNull();
        }
        foreach (var cut in targets)
        {
            var entry = cut.ReceiptEntry ?? throw new InvalidOperationException(P.Missing);
            var operation = entry.Operation ?? throw new InvalidOperationException(P.Missing);
            var phase = global::KeyLoad.Core.Features.ClusterRouting.Serialization.PartitionMovementNativePayload.Read(operation);
            await Assert.That(cut.Request.ReceiptToken).IsEqualTo(token);
            await Assert.That(token.Position <= cut.CommittedIndex).IsTrue();
            await Assert.That(entry.Term).IsGreaterThan(P.InitialCleanup);
            await Assert.That(entry.Index).IsEqualTo(token.Position);
            await Assert.That(operation.Kind).IsEqualTo(OperationKind.PartitionMovementPhase);
            await Assert.That(operation.PrincipalId).IsEqualTo(original.Operation.PrincipalId);
            await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.Install);
            await Assert.That(phase.MoveId).IsEqualTo(seed.FirstRequest.MoveId);
            await Assert.That(operation.Id).IsEqualTo(PartitionMovementParentPhaseIds.For(seed.FirstRequest,
                original.Operation.PrincipalId, PartitionMovementParentPhaseRole.Install, phase.PageOrdinal));
            await SqlRf3Protocol.EqualAsync(token, new CommitToken(cut.Identity.Incarnation,
                seed.Partition.AtomicPartitionId, entry.Index, target.PlacementEpoch));
        }
    }

    private static async Task PrincipalAsync(ControlledRetireAspireOriginal original, ReadOnlyMemory<byte> bytes)
    {
        var principal = NativeSerialization.Deserialize<PrincipalRecord>(bytes.Span);
        await Assert.That(principal.Id).IsEqualTo(original.Operation.PrincipalId);
        await Assert.That(principal.ClusterAdministrator).IsTrue();
        await Assert.That(principal.Revoked).IsFalse();
        await Assert.That(principal.ExpiresAt is null || principal.ExpiresAt > TimeProvider.System.GetUtcNow()).IsTrue();
    }

}
