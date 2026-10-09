using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Accounts only actual acknowledged old-Advance reconciliation; every forward-effect row is immutable.</summary>
internal static class PartitionMovementCapturePointerRf3Cut
{

    internal static async Task RequireOriginalAsync(PartitionMovementPublicParentRf3NativeCut[] cuts,
        PartitionMoveRequest request, Guid advance)
    {
        var controls = cuts.Where(cut => cut.Header is not null).ToArray();
        await Assert.That(controls.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var cut in controls)
        {
            var header = cut.Header!;
            await Assert.That(header.PendingOriginalPhaseCommandId).IsEqualTo((Guid?)advance);
            await Assert.That(header.OriginalCapturePhaseCommandId).IsNotNull();
            var capture = PartitionMovementCapturePointerRf3Fault.Read<PartitionMoveParentPhase>(cut,
                PartitionMoveParentKeys.Phase(request.Partition, request.MoveId, header.OriginalCapturePhaseCommandId!.Value));
            await Assert.That(capture.Stage).IsEqualTo(PartitionMovePeerStage.Capture);
            await Assert.That(capture.OriginalPhase).IsNotNull();
            await Assert.That(capture.OriginalResult).IsNotNull();
            await Assert.That(capture.OriginalResult!.Error).IsNull();
            await Assert.That(capture.ObservationCheckpointReceipt).IsNotNull();
            await Assert.That(capture.CaptureProofCheckpointReceipt).IsNotNull();
            await Assert.That(capture.OriginalCaptureWitness).IsNotNull();
            await Assert.That(capture.OriginalDescriptor).IsNotNull();
            await Assert.That(capture.OriginalFence).IsNotNull();
            await Assert.That(capture.OriginalReceiverIssuanceWitness).IsNotNull();
            await Assert.That(capture.ReceiverIssuanceCheckpointReceipt).IsNotNull();
            await Assert.That(cut.OriginalPhase!.Stage).IsEqualTo(PartitionMovePeerStage.ControlAdvance);
            await Assert.That(cut.OriginalPhase.OriginalResult).IsNull();
            var outcome = OriginalOutcome(cut, request, advance);
            await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
            await SqlRf3Protocol.EqualAsync(request.Partition, outcome.Partition);
            await Assert.That(outcome.PolicyEpoch).IsEqualTo(cut.OriginalPhase.OriginalIssuancePolicyEpoch);
            await Assert.That(outcome.Result.Error).IsNull();
            var result = outcome.Result.Get<PartitionMovePhaseResult>();
            await Assert.That(result.Journal.CommandId).IsEqualTo(advance);
            await Assert.That(result.Control!.Phase).IsEqualTo(PartitionMovePhase.Captured);
            await Assert.That(result.Journal.AppliedPosition).IsLessThanOrEqualTo(PartitionMovementCapturePointerRf3Fault.Applied(cut));
            await RequireNoForwardPhaseAsync(cut, request);
        }
    }

    private static StoredOutcome OriginalOutcome(PartitionMovementPublicParentRf3NativeCut cut,
        PartitionMoveRequest request, Guid advance)
        => PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(cut, KeySpace.PartitionOutcome(request.Partition,
            PartitionMovementCapturePointerRf3Producer.Administrator, advance));

    private static async Task RequireNoForwardPhaseAsync(PartitionMovementPublicParentRf3NativeCut cut,
        PartitionMoveRequest request)
    {
        var prefix = Convert.ToHexString(PartitionMoveParentKeys.PhasePrefix(request.Partition, request.MoveId));
        var phases = PartitionMovementCapturePointerRf3Fault.Rows(cut).Where(row => row.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(row => NativeSerialization.Deserialize<PartitionMoveParentPhase>(Convert.FromHexString(row.Value))).ToArray();
        await Assert.That(phases.Any(phase => phase.Stage is PartitionMovePeerStage.StagePage or PartitionMovePeerStage.Install
            or PartitionMovePeerStage.Retire or PartitionMovePeerStage.PublishWitness)).IsFalse();
        await Assert.That(phases.Any(phase => phase.Stage == PartitionMovePeerStage.ControlAuthorize
            && NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(phase.OriginalPhase!.Body.Span).Phase.Stage
                != PartitionMovePeerStage.Fence
            && NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(phase.OriginalPhase!.Body.Span).Phase.Stage
                != PartitionMovePeerStage.Capture)).IsFalse();
    }

    internal static async Task RequireNoForwardEffectAsync(TwoRf3MembershipWave wave, PartitionMoveRequest request,
        Guid advance, PartitionMovementPublicParentRf3NativeCut[] before, PartitionMovementPublicParentRf3NativeCut[] after)
    {
        foreach (var original in before)
        {
            var actual = after.Single(cut => cut.Node == original.Node);
            var entries = PartitionMovementCapturePointerRf3Fault.AppliedEntries(wave, original, actual);
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            if (entries.Length != PartitionMoveProtocol.EmptyCount)
            { allowed.Add(Convert.ToHexString(KeySpace.AppliedBytes)); }
            var receipts = await RequireJournalAsync(request, advance, original, actual, entries, allowed);
            if (original.Header is not null)
            {
                await RequireObservedPhaseAsync(request, advance, original, actual, receipts, allowed);
                await RequireNoForwardPhaseAsync(actual, request);
            }
            await RequireExactRemainingRowsAsync(original, actual, allowed);
            await Assert.That(actual.StorePosition - original.StorePosition).IsEqualTo((long)entries.Length);
        }
    }

    private static async Task<PartitionMoveJournalReceipt[]> RequireJournalAsync(PartitionMoveRequest request,
        Guid advance, PartitionMovementPublicParentRf3NativeCut before, PartitionMovementPublicParentRf3NativeCut after,
        ReplicaEntry[] entries, HashSet<string> allowed)
    {
        var receipts = new List<PartitionMoveJournalReceipt>();
        foreach (var entry in entries)
        {
            if (entry.Operation is not { } operation)
            { continue; } // Actual native election no-op, not an inferred effect.
            await Assert.That(before.Header).IsNotNull();
            await Assert.That(operation.Kind).IsEqualTo(OperationKind.PartitionMovementPhase);
            var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
            await Assert.That(operation.PrincipalId).IsEqualTo(PartitionMovementCapturePointerRf3Producer.Administrator);
            await Assert.That(phase.MoveId).IsEqualTo(request.MoveId);
            await SqlRf3Protocol.EqualAsync(request.Partition, phase.Partition);
            await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.ControlCheckpoint);
            var body = NativeSerialization.Deserialize<PartitionMoveCheckpointBody>(phase.Body.Span);
            await Assert.That(body.Action).IsEqualTo(PartitionMoveCheckpointAction.Observe);
            await Assert.That(body.OriginalPhaseCommandId).IsEqualTo(advance);
            await Assert.That(body.NextOriginalPhase).IsNull();
            await Assert.That(body.NextOriginalPhaseCommandId).IsNull();
            await SqlRf3Protocol.EqualAsync(OriginalOutcome(before, request, advance).Result, body.ObservedOriginalResult);
            var key = KeySpace.PartitionOutcome(request.Partition, operation.PrincipalId, operation.Id);
            await Assert.That(operation.Id).IsNotEqualTo(advance);
            await Assert.That(PartitionMovementCapturePointerRf3Fault.Rows(before).ContainsKey(Convert.ToHexString(key))).IsFalse();
            var stored = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(after, key);
            await Assert.That(stored.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
            await SqlRf3Protocol.EqualAsync(request.Partition, stored.Partition);
            await Assert.That(stored.Fingerprint).IsEqualTo(JsonData.Fingerprint(new
            { operation.Id, operation.Kind, operation.PrincipalId, operation.PayloadJson }));
            allowed.Add(Convert.ToHexString(key));
            var locator = CommandOutcomePartitionLocatorSerialization.ScopedKey(request.Partition, operation.PrincipalId, operation.Id);
            var rows = PartitionMovementCapturePointerRf3Fault.Rows(after);
            await Assert.That(rows[Convert.ToHexString(locator)]).IsEqualTo(Convert.ToHexString(key));
            allowed.Add(Convert.ToHexString(locator));
            allowed.Add(Convert.ToHexString(KeySpace.ClockBytes));
            if (stored.Result.Error is null)
            {
                var result = stored.Result.Get<PartitionMovePhaseResult>();
                await Assert.That(result.Journal.CommandId).IsEqualTo(operation.Id);
                await Assert.That(result.Journal.AppliedPosition).IsEqualTo(entry.Index);
                receipts.Add(result.Journal);
            }
        }
        if (entries.Any(entry => entry.Operation is not null))
        {
            var beforeClock = PartitionMovementCapturePointerRf3Fault.Read<DateTimeOffset>(before, KeySpace.ClockBytes);
            var expectedClock = entries.Where(entry => entry.Operation is not null).Select(entry => entry.Operation!.EvaluatedAt)
                .Append(beforeClock).Max();
            await Assert.That(PartitionMovementCapturePointerRf3Fault.Read<DateTimeOffset>(after, KeySpace.ClockBytes)).IsEqualTo(expectedClock);
        }
        return receipts.ToArray();
    }

    private static async Task RequireObservedPhaseAsync(PartitionMoveRequest request, Guid advance,
        PartitionMovementPublicParentRf3NativeCut before, PartitionMovementPublicParentRf3NativeCut after,
        PartitionMoveJournalReceipt[] receipts, HashSet<string> allowed)
    {
        if (receipts.Length == PartitionMoveProtocol.EmptyCount)
        { return; }
        await Assert.That(receipts.Length).IsEqualTo(PartitionMoveProtocol.SequenceStep);
        var oldPhase = before.OriginalPhase!;
        var newPhase = after.OriginalPhase!;
        var result = OriginalOutcome(before, request, advance).Result;
        var value = result.Get<PartitionMovePhaseResult>();
        var expected = oldPhase with
        {
            OriginalResult = result,
            ObservationCheckpointReceipt = receipts.Single(),
            OriginalFence = oldPhase.OriginalFence ?? value.Fence
        };
        await SqlRf3Protocol.EqualAsync(expected, newPhase);
        var header = after.Header!;
        await SqlRf3Protocol.EqualAsync(before.Header! with
        {
            Generation = checked(before.Header!.Generation + PartitionMoveProtocol.SequenceStep),
            PendingOriginalPhaseCommandId = null,
            RetainedMetadataBytes = header.RetainedMetadataBytes
        }, header);
        var baseBytes = checked(before.Header!.RetainedMetadataBytes + NativeSerialization.Measure(newPhase)
            - NativeSerialization.Measure(oldPhase) - NativeSerialization.Measure(before.Header));
        await Assert.That(header.RetainedMetadataBytes).IsEqualTo(checked(baseBytes + NativeSerialization.Measure(header)));
        allowed.Add(Convert.ToHexString(PartitionMoveParentKeys.Header(request.Partition, request.MoveId)));
        allowed.Add(Convert.ToHexString(PartitionMoveParentKeys.Phase(request.Partition, request.MoveId, advance)));
    }

    internal static async Task RequireExactRemainingRowsAsync(PartitionMovementPublicParentRf3NativeCut before,
        PartitionMovementPublicParentRf3NativeCut after, HashSet<string> allowed)
    {
        var oldRows = PartitionMovementCapturePointerRf3Fault.Rows(before);
        var newRows = PartitionMovementCapturePointerRf3Fault.Rows(after);
        foreach (var key in oldRows.Keys.Union(newRows.Keys).Where(key => !allowed.Contains(key)))
        {
            await Assert.That(newRows.TryGetValue(key, out var actual)).IsEqualTo(oldRows.TryGetValue(key, out var original));
            await Assert.That(string.Equals(actual, original, StringComparison.Ordinal)).IsTrue();
        }
        await Assert.That(after.OutstandingMoveGrants).IsEqualTo(before.OutstandingMoveGrants);
        await Assert.That(after.OutstandingPrincipalGrants).IsEqualTo(before.OutstandingPrincipalGrants);
        await Assert.That(after.OutstandingDatabaseGrants).IsEqualTo(before.OutstandingDatabaseGrants);
    }
}
