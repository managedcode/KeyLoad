using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Native failed effects leave every target row except exact issuance/outcome/clock/apply metadata unchanged.</summary>
internal static class PartitionMovementFinalInstallFrameRf3Assertions
{
    internal static async Task RequireDeniedAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, PartitionMovementPublicParentRf3NativeCut[] after)
    {
        foreach (var before in precut.Cuts)
        {
            var actual = after.Single(cut => cut.Node == before.Node);
            var entries = PartitionMovementCapturePointerRf3Fault.AppliedEntries(wave, before, actual);
            await Assert.That(actual.StorePosition - before.StorePosition).IsEqualTo((long)entries.Length);
            await RequireBusinessAsync(seed, before, actual);
            if (before.Header is null)
            { await RequireTargetAsync(seed, precut, before, actual, entries); }
            else
            {
                await Assert.That(actual.Header!.TerminalResult).IsNull();
                await Assert.That(actual.Header.OriginalCapturePhaseCommandId).IsEqualTo(before.Header.OriginalCapturePhaseCommandId);
                await RequireControlAsync(seed, precut, before, actual);
                await PartitionMovementEmbeddedFailureRf3Assertions.RequireAsync(seed, precut, actual, after, entries);
                foreach (var entry in entries.Where(entry => entry.Operation is not null))
                {
                    var operation = entry.Operation!;
                    await Assert.That(operation.Kind).IsEqualTo(OperationKind.PartitionMovementPhase);
                    var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
                    await Assert.That(phase.MoveId).IsEqualTo(seed.FirstRequest.MoveId);
                    await Assert.That(phase.Partition).IsEqualTo(seed.Partition);
                    await Assert.That(phase.Stage is PartitionMovePeerStage.ControlAuthorize or PartitionMovePeerStage.ControlCheckpoint).IsTrue();
                }
            }
        }
    }

    private static async Task RequireControlAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, PartitionMovementPublicParentRf3NativeCut before,
        PartitionMovementPublicParentRf3NativeCut actual)
    {
        var originalRows = PartitionMovementCapturePointerRf3Fault.Rows(before);
        var currentRows = PartitionMovementCapturePointerRf3Fault.Rows(actual);
        var record = Convert.ToHexString(KeySpace.Partition(PartitionMoveProtocol.RecordSpace, seed.Partition, seed.FirstRequest.MoveId));
        var fence = Convert.ToHexString(PartitionMoveSourceFenceStorage.Key(seed.Partition));
        await Assert.That(currentRows[record]).IsEqualTo(originalRows[record]);
        await Assert.That(currentRows[fence]).IsEqualTo(originalRows[fence]);
        await Assert.That(actual.OutstandingMoveGrants).IsEqualTo(checked(before.OutstandingMoveGrants + PartitionMoveProtocol.SequenceStep));
        await Assert.That(actual.OutstandingPrincipalGrants).IsEqualTo(before.OutstandingPrincipalGrants + PartitionMoveProtocol.SequenceStep);
        await Assert.That(actual.OutstandingDatabaseGrants).IsEqualTo(checked(before.OutstandingDatabaseGrants + PartitionMoveProtocol.SequenceStep));
        var retained = actual.OriginalPhase!;
        await Assert.That(retained.OriginalPhaseCommandId).IsEqualTo(precut.EffectId);
        await Assert.That(retained.OriginalGrant!.GrantId).IsEqualTo(precut.GrantId);
        await Assert.That(retained.OriginalResult!.Error).IsEqualTo((ErrorCode?)ErrorCode.ResourceExhausted);
        await Assert.That(retained.OriginalResult.NativeValue).IsNull();
        await Assert.That(retained.OriginalResult.Json).IsNull();
        await Assert.That(retained.ObservationCheckpointReceipt).IsNotNull();
        await Assert.That(actual.OriginalControlGrant!.Settlement).IsNull();
        await Assert.That(actual.OriginalControlGrant.AbortDisposition).IsNull();
        await Assert.That(actual.OriginalControlGrant.RetireCancellationDisposition).IsNull();
    }

    private static async Task RequireBusinessAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3NativeCut before, PartitionMovementPublicParentRf3NativeCut after)
    {
        foreach (var family in PartitionRecordFamilies.All)
        {
            var prefix = Convert.ToHexString(KeySpace.Partition(family, seed.Partition));
            var old = before.Rows.Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            var current = after.Rows.Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (family is PartitionRecordFamilies.OutcomeV2 or PartitionRecordFamilies.OutcomeLocatorV2)
            {
                foreach (var row in old)
                { await Assert.That(current.Contains(row, StringComparer.Ordinal)).IsTrue(); }
            }
            else
            { await SqlRf3Protocol.EqualAsync(old, current); }
        }
    }

    private static async Task RequireTargetAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, PartitionMovementPublicParentRf3NativeCut before,
        PartitionMovementPublicParentRf3NativeCut actual, KeyLoad.Replication.ReplicaEntry[] entries)
    {
        var issued = actual.OriginalReceiverIssuance ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        await Assert.That(issued.OriginalPhaseCommandId).IsEqualTo(precut.EffectId);
        await Assert.That(issued.OriginalStage).IsEqualTo(PartitionMovePeerStage.Install);
        await Assert.That(issued.OriginalPageOrdinal).IsEqualTo(precut.FinalOrdinal);
        await Assert.That(actual.OriginalNativeResult!.Error).IsEqualTo((ErrorCode?)ErrorCode.ResourceExhausted);
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            Convert.ToHexString(KeySpace.AppliedBytes), Convert.ToHexString(KeySpace.ClockBytes),
            Convert.ToHexString(PartitionMoveReceiverIssuanceStorage.Key(seed.Partition, seed.FirstRequest.MoveId, precut.EffectId)),
            Convert.ToHexString(PartitionMoveReceiverIssuanceStorage.CountKey(seed.Partition, seed.FirstRequest.MoveId)),
            Convert.ToHexString(PartitionMoveReceiverIssuanceStorage.BytesKey(seed.Partition, seed.FirstRequest.MoveId))
        };
        foreach (var entry in entries.Where(entry => entry.Operation is not null))
        {
            var operation = entry.Operation!;
            await Assert.That(operation.Id == issued.IssuanceCommandId || operation.Id == precut.EffectId).IsTrue();
            await Assert.That(operation.PrincipalId).IsEqualTo(issued.ReceiverPrincipalId);
            var key = KeySpace.PartitionOutcome(seed.Partition, operation.PrincipalId, operation.Id);
            var result = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(actual, key);
            await SqlRf3Protocol.EqualAsync(seed.Partition, result.Partition);
            await Assert.That(result.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
            if (operation.Id == precut.EffectId)
            { await Assert.That(result.Result.Error).IsEqualTo((ErrorCode?)ErrorCode.ResourceExhausted); }
            else
            { await Assert.That(result.Result.Error).IsNull(); }
            allowed.Add(Convert.ToHexString(key));
            allowed.Add(Convert.ToHexString(CommandOutcomePartitionLocatorSerialization.ScopedKey(seed.Partition,
                operation.PrincipalId, operation.Id)));
        }
        await PartitionMovementCapturePointerRf3Cut.RequireExactRemainingRowsAsync(before, actual, allowed);
    }
}
