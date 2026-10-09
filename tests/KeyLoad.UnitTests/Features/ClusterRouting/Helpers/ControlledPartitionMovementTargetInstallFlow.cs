using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Installs original staged pages only after genuine A grants and acknowledges each actual B journal.</summary>
internal static class ControlledPartitionMovementTargetInstallFlow
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ServerRuntimeOptions sourceRuntime,
        ServerRuntimeOptions targetRuntime, PartitionMovementPeerAdmission sourceAdmission,
        PartitionMovementPeerAdmission targetAdmission, ControlledPartitionMovementLoopbackCorpus corpus,
        PartitionMovePhaseResult captured, PartitionMoveSourceFenceRecord originalFence,
        PartitionMovementCaptureHandle originalHandle,
        string originalCallerAddress, DateTimeOffset wholeExpiresAt, CancellationToken cancellationToken)
    {
        PartitionMovePhaseResult? terminal = null;
        for (var ordinal = 0; ordinal <= originalHandle.PageCount; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var phaseExpiry = ControlledPartitionMovementFirstPhaseExpiry.Create(source, sourceRuntime,
                wholeExpiresAt, cancellationToken);
            var authorization = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source,
                sourceRuntime, sourceAdmission, ControlledPartitionMovementTargetInstallRequest.Authorize(
                    captured, originalFence, originalHandle, ordinal, corpus, originalCallerAddress,
                    phaseExpiry), cancellationToken);
            await Assert.That(authorization.Error).IsNull();
            var issued = authorization.Get<PartitionMovePhaseResult>();
            await Assert.That(issued.Grant!.ExpiresAt).IsEqualTo(phaseExpiry);
            var request = ControlledPartitionMovementTargetInstallRequest.Install(captured, originalFence,
                originalHandle, ordinal, issued, corpus,
                originalCallerAddress, issued.Grant!.ExpiresAt);
            var staged = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(target, targetRuntime,
                targetAdmission, request, cancellationToken);
            await Assert.That(staged.Error).IsNull();
            var actual = staged.Get<PartitionMovePhaseResult>();
            await Assert.That(actual.Stage).IsEqualTo(PartitionMovePeerStage.Install);
            if (ordinal < originalHandle.PageCount)
            { await Assert.That(actual.InstalledReceipt).IsNull(); }
            else
            {
                await Assert.That(actual.InstalledReceipt).IsNotNull();
                await ControlledPartitionMovementTargetInstallReceipt.AssertAsync(target, corpus,
                    actual, originalHandle.PageCount);
                terminal = actual;
            }
            await Assert.That(actual.PublishedPlacement).IsNull();
            await Assert.That(actual.Journal.CommandId).IsEqualTo(request.CommandId);
            await Assert.That(JsonDefaults.Serialize(actual.Journal.PhysicalOwner)
                .SequenceEqual(JsonDefaults.Serialize(corpus.Destination.Owner))).IsTrue();
            await AcknowledgeAsync(source, sourceRuntime, sourceAdmission, corpus, captured,
                actual, ordinal, originalCallerAddress, wholeExpiresAt, cancellationToken);
        }
        return terminal ?? throw new InvalidOperationException("The actual terminal Install receipt is absent.");
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
            ControlledPartitionMovementTargetPhaseIds.InstallGrant(ordinal), staged.Journal));
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            captured.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlAcknowledge,
            ControlOrdinal, expiry, Guid.NewGuid(), body);
        var result = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(ControlledPartitionMovementTargetPhaseIds.InstallAcknowledgement(ordinal), envelope, null,
                corpus.Control.Owner.VoterIds.First(), callerAddress, PartitionMovementTransportAction.Apply,
                Guid.Empty, ControlOrdinal), cancellationToken);
        await Assert.That(result.Error).IsNull();
        var grant = result.Get<PartitionMovePhaseResult>().Grant
            ?? throw new InvalidOperationException("Actual target-install settlement is absent.");
        await Assert.That(grant.GrantId).IsEqualTo(ControlledPartitionMovementTargetPhaseIds.InstallGrant(ordinal));
        await Assert.That(JsonDefaults.Serialize(grant.Settlement)
            .SequenceEqual(JsonDefaults.Serialize(staged.Journal))).IsTrue();
    }
}
