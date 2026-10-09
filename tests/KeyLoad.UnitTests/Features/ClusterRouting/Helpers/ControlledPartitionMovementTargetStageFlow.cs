using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Stages original captured pages only after genuine A grants and acknowledges each actual B journal.</summary>
internal static class ControlledPartitionMovementTargetStageFlow
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ServerRuntimeOptions sourceRuntime,
        ServerRuntimeOptions targetRuntime, PartitionMovementPeerAdmission sourceAdmission,
        PartitionMovementPeerAdmission targetAdmission, ControlledPartitionMovementLoopbackCorpus corpus,
        PartitionMovePhaseResult captured, PartitionMoveSourceFenceRecord originalFence,
        PartitionMovementCaptureHandle originalHandle, PartitionMovementPageResult[] originalPages,
        string originalCallerAddress, DateTimeOffset wholeExpiresAt, CancellationToken cancellationToken)
    {
        var originalModel = ControlledPartitionMovementTargetModelImage.Read(target);
        foreach (var page in originalPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var phaseExpiry = ControlledPartitionMovementFirstPhaseExpiry.Create(source, sourceRuntime,
                wholeExpiresAt, cancellationToken);
            var authorization = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source,
                sourceRuntime, sourceAdmission, ControlledPartitionMovementTargetStageRequest.Authorize(
                    captured, originalFence, originalHandle, page, corpus, originalCallerAddress,
                    phaseExpiry), cancellationToken);
            await Assert.That(authorization.Error).IsNull();
            var issued = authorization.Get<PartitionMovePhaseResult>();
            await Assert.That(issued.Grant!.ExpiresAt).IsEqualTo(phaseExpiry);
            var request = ControlledPartitionMovementTargetStageRequest.Stage(captured, originalFence,
                originalHandle, page, issued, corpus,
                originalCallerAddress, issued.Grant!.ExpiresAt);
            var staged = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(target, targetRuntime,
                targetAdmission, request, cancellationToken);
            await Assert.That(staged.Error).IsNull();
            var actual = staged.Get<PartitionMovePhaseResult>();
            await Assert.That(actual.Stage).IsEqualTo(PartitionMovePeerStage.StagePage);
            await Assert.That(actual.InstalledReceipt).IsNull();
            await Assert.That(actual.PublishedPlacement).IsNull();
            await Assert.That(actual.Journal.CommandId).IsEqualTo(request.CommandId);
            await Assert.That(JsonDefaults.Serialize(actual.Journal.PhysicalOwner)
                .SequenceEqual(JsonDefaults.Serialize(corpus.Destination.Owner))).IsTrue();
            await Assert.That(ControlledPartitionMovementTargetModelImage.Read(target)
                .SequenceEqual(originalModel, StringComparer.Ordinal)).IsTrue();
            var originalBytes = NativeSerialization.Serialize(staged);
            var originalImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
            var originalPosition = target.Store.Position;
            var originalIndex = target.Journal.Log.State.LastIndex;
            var replay = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(target, targetRuntime,
                targetAdmission, request with { Envelope = request.Envelope with { Nonce = Guid.NewGuid() } },
                cancellationToken);
            await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(originalBytes)).IsTrue();
            await Assert.That(target.Store.Position).IsEqualTo(originalPosition);
            await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(originalIndex);
            await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store)
                .SequenceEqual(originalImage, StringComparer.Ordinal)).IsTrue();
            await AcknowledgeAsync(source, sourceRuntime, sourceAdmission, corpus, captured,
                actual, page.Ordinal, originalCallerAddress, wholeExpiresAt, cancellationToken);
        }
    }

    private static async Task AcknowledgeAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult captured,
        PartitionMovePhaseResult staged, int ordinal, string callerAddress, DateTimeOffset wholeExpiresAt,
        CancellationToken cancellationToken)
    {
        var control = captured.Control ?? throw new InvalidOperationException("Actual Captured control is absent.");
        var expiry = ControlledPartitionMovementFirstPhaseExpiry.Create(source, runtime,
            wholeExpiresAt, cancellationToken);
        var body = NativeSerialization.Serialize(new PartitionMoveAcknowledgeBody(
            ControlledPartitionMovementTargetPhaseIds.Grant(ordinal), staged.Journal));
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            captured.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlAcknowledge,
            ControlOrdinal, expiry, Guid.NewGuid(), body);
        var result = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(ControlledPartitionMovementTargetPhaseIds.Acknowledgement(ordinal), envelope, null,
                corpus.Control.Owner.VoterIds.First(), callerAddress, PartitionMovementTransportAction.Apply,
                Guid.Empty, ControlOrdinal), cancellationToken);
        await Assert.That(result.Error).IsNull();
        var grant = result.Get<PartitionMovePhaseResult>().Grant
            ?? throw new InvalidOperationException("Actual target-stage settlement is absent.");
        await Assert.That(grant.GrantId).IsEqualTo(ControlledPartitionMovementTargetPhaseIds.Grant(ordinal));
        await Assert.That(JsonDefaults.Serialize(grant.Settlement)
            .SequenceEqual(JsonDefaults.Serialize(staged.Journal))).IsTrue();
    }
}
