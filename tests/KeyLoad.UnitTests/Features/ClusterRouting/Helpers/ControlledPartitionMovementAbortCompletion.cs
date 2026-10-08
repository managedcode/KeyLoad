using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Disposes the single retained capture grant and finalizes using both real terminal journals and original bodies.</summary>
internal static class ControlledPartitionMovementAbortCompletion
{
    private const int Version = 1;
    private const int OriginalBatch = 0;
    private const int CompletedBatch = 1;
    private static readonly Guid CancelId = Guid.Parse("5c7ce06c-d0ef-480a-8697-0cd08ba4e098");
    private static readonly Guid FinalizeId = Guid.Parse("e3718f07-66ea-4b5a-85a3-ec2b9bd697f0");

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult aborting,
        PartitionMoveCompletionBody originalCompletion, string callerAddress, DateTimeOffset expiry,
        CancellationToken cancellationToken)
    {
        var body = NativeSerialization.Serialize(originalCompletion);
        var canceled = await SubmitAsync(source, runtime, admission, corpus, aborting, body,
            PartitionMovePeerStage.ControlCancelGrants, CancelId, callerAddress, expiry, cancellationToken);
        var cleanup = new PartitionMoveCleanupState(Version, originalCompletion.Control.MoveId,
            originalCompletion.Control.Partition, aborting.Journal.ControlIntentDigest,
            PartitionMovePeerStage.ControlCancelGrants, PartitionMoveCleanupRole.Source,
            OriginalBatch, canceled.Journal, CompletedBatch);
        await Assert.That(JsonDefaults.Serialize(canceled.Cleanup)
            .SequenceEqual(JsonDefaults.Serialize(cleanup))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(canceled.Control)
            .SequenceEqual(JsonDefaults.Serialize(originalCompletion.Control))).IsTrue();
        var actual = await SubmitAsync(source, runtime, admission, corpus, aborting, body,
            PartitionMovePeerStage.ControlFinalizeAbort, FinalizeId, callerAddress, expiry, cancellationToken);
        var completed = originalCompletion.Control with { Phase = PartitionMovePhase.Aborted };
        await Assert.That(JsonDefaults.Serialize(actual.Control)
            .SequenceEqual(JsonDefaults.Serialize(completed))).IsTrue();
        await Assert.That(actual.Fence).IsNull();
        await Assert.That(actual.InstalledReceipt).IsNull();
        await Assert.That(actual.PublishedPlacement).IsNull();
        return actual;
    }

    private static async Task<PartitionMovePhaseResult> SubmitAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult aborting,
        byte[] originalBody, PartitionMovePeerStage stage, Guid commandId, string callerAddress,
        DateTimeOffset expiry, CancellationToken cancellationToken)
    {
        var control = aborting.Control ?? throw new InvalidOperationException("Actual Aborting control is absent.");
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            aborting.Journal.ControlIntentDigest, stage, OriginalBatch, expiry, Guid.NewGuid(), originalBody);
        var outcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(commandId, envelope, null, corpus.Control.Owner.VoterIds.First(), callerAddress,
                PartitionMovementTransportAction.Apply, Guid.Empty, OriginalBatch), cancellationToken);
        await Assert.That(outcome.Error).IsNull();
        var actual = outcome.Get<PartitionMovePhaseResult>();
        await Assert.That(actual.Stage).IsEqualTo(stage);
        await Assert.That(actual.Journal.CommandId).IsEqualTo(commandId);
        return actual;
    }
}
