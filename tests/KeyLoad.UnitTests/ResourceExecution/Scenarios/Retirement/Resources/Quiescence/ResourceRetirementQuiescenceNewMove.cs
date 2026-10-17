using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceNewMove
{
    internal static async Task RefuseAsync(ResourceRetirementQuiescenceState state, ServerRuntimeOptions runtime,
        PartitionMovementPeerAdmission admission, CancellationToken token)
    {
        var placement = ControlledPartitionMovementPrepareAssertions.Placement(state.Corpus) with
        {
            Partition = OtherPartition
        };
        var body = NativeSerialization.Serialize(new PartitionMovePrepareBody(Root,
            new(NewMoveId, OtherPartition, state.Target.PhysicalOwner.PhysicalShardId, Zero, PartitionMoveMode.Transfer)));
        var envelope = new PartitionMovePeerEnvelope(ResourceRetirementQuiescenceData.Version, NewMoveId, OtherPartition, state.Corpus.Control.Owner,
            placement, state.Corpus.Destination.Owner, Convert.ToHexStringLower(SHA256.HashData(body)),
            PartitionMovePeerStage.ControlPrepare, Zero, state.ExpiresAt, Guid.NewGuid(), body);
        var request = new PartitionMovementTransportRequest(NewMoveCommandId, envelope, null,
            state.Source.PhysicalOwner.VoterIds.First(), ControlledPartitionMovementPrepareRequest.CallerAddress(state.Listeners),
            PartitionMovementTransportAction.Apply, Guid.Empty, Zero);
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var outcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(state.Source, runtime, admission, request, token);
        await Assert.That(outcome.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(outcome.SafeDetail).IsEqualTo(Retiring);
        await Assert.That(outcome.Json).IsNull();
        await Assert.That(outcome.NativeValue).IsNull();
        var original = state.Source.Journal.Log.Read(state.Source.Journal.Log.State.LastIndex, One,
            state.Source.Journal.Configuration.MaxAppendBytes).Single().Operation ?? throw new InvalidOperationException(Invalid);
        await Assert.That(original.Id).IsEqualTo(NewMoveCommandId);
        state.Originals.Add((state.Source, original, outcome));
        await FailureDeltaAsync(state.Source, source, original);
        await target.AssertUnchangedAsync(state.Target);
    }

    private static async Task FailureDeltaAsync(ControlledPartitionMovementNode owner,
        ControlledPartitionMovementExpiryOwnerState before, ReplicatedOperation original)
    {
        var outcome = KeySpace.PartitionOutcome(OtherPartition, original.PrincipalId, original.Id);
        var locator = KeySpace.OutcomeLocatorV2(OtherPartition, original.PrincipalId, original.Id);
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(locator))!.AsSpan().SequenceEqual(outcome)).IsTrue();
        var allowed = new[] { KeySpace.AppliedBytes, KeySpace.ClockBytes, outcome, locator }
            .Select(key => Convert.ToHexString(key) + ":").ToArray();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(owner.Store)
            .Where(row => !allowed.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal)))
            .SequenceEqual(before.Image.Where(row => !allowed.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))),
                StringComparer.Ordinal)).IsTrue();
        await Assert.That(owner.Store.Position).IsEqualTo(before.Position + One);
        await Assert.That(owner.Journal.Log.State.LastIndex).IsEqualTo(before.LastIndex + One);
        await Assert.That(owner.Journal.Log.State.CommittedIndex).IsEqualTo(before.CommittedIndex + One);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(before.LastIndex + One);
    }
}
