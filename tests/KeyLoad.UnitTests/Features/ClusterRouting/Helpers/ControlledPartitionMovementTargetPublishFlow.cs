using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Records technical B publication only from genuine protected A lineage and ACKs its actual journal.</summary>
internal static class ControlledPartitionMovementTargetPublishFlow
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ServerRuntimeOptions sourceRuntime,
        ServerRuntimeOptions targetRuntime, PartitionMovementPeerAdmission sourceAdmission,
        PartitionMovementPeerAdmission targetAdmission, ControlledPartitionMovementLoopbackCorpus corpus,
        PartitionMovePhaseResult finalized, ImmutableArray<ResourceDefinition> originalResources,
        string callerAddress, DateTimeOffset expiry, CancellationToken cancellationToken)
    {
        var publication = await ControlledPartitionMovementPublicationRead.ReadAsync(source, corpus, finalized);
        var authorization = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, sourceRuntime,
            sourceAdmission, ControlledPartitionMovementTargetPublishRequest.Authorize(finalized,
                publication, originalResources, corpus, callerAddress, expiry), cancellationToken);
        await Assert.That(authorization.Error).IsNull();
        var request = ControlledPartitionMovementTargetPublishRequest.Publish(finalized, publication,
            originalResources, authorization.Get<PartitionMovePhaseResult>(), corpus, callerAddress, expiry);
        var outcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(target, targetRuntime,
            targetAdmission, request, cancellationToken);
        await Assert.That(outcome.Error).IsNull();
        var actual = outcome.Get<PartitionMovePhaseResult>();
        await Assert.That(actual.Stage).IsEqualTo(PartitionMovePeerStage.PublishWitness);
        await Assert.That(actual.Journal.CommandId).IsEqualTo(ControlledPartitionMovementTargetPhaseIds.PublishCommand());
        await Assert.That(JsonDefaults.Serialize(actual.Control).SequenceEqual(JsonDefaults.Serialize(finalized.Control))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(actual.PublishedPlacement)
            .SequenceEqual(JsonDefaults.Serialize(publication.Placement))).IsTrue();
        await Assert.That(actual.InstalledReceipt).IsNull();
        await AcknowledgeAsync(source, sourceRuntime, sourceAdmission, corpus, finalized,
            actual, callerAddress, expiry, cancellationToken);
        return actual;
    }

    private static async Task AcknowledgeAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult captured,
        PartitionMovePhaseResult staged, string callerAddress, DateTimeOffset expiry,
        CancellationToken cancellationToken)
    {
        var control = captured.Control ?? throw new InvalidOperationException("Actual Captured control is absent.");
        var body = NativeSerialization.Serialize(new PartitionMoveAcknowledgeBody(
            ControlledPartitionMovementTargetPhaseIds.PublishGrant(), staged.Journal));
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            captured.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlAcknowledge,
            ControlOrdinal, expiry, Guid.NewGuid(), body);
        var result = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(ControlledPartitionMovementTargetPhaseIds.PublishAcknowledgement(), envelope, null,
                corpus.Control.Owner.VoterIds.First(), callerAddress, PartitionMovementTransportAction.Apply,
                Guid.Empty, ControlOrdinal), cancellationToken);
        await Assert.That(result.Error).IsNull();
        var grant = result.Get<PartitionMovePhaseResult>().Grant
            ?? throw new InvalidOperationException("Actual target-publication settlement is absent.");
        await Assert.That(grant.GrantId).IsEqualTo(ControlledPartitionMovementTargetPhaseIds.PublishGrant());
        await Assert.That(JsonDefaults.Serialize(grant.Settlement)
            .SequenceEqual(JsonDefaults.Serialize(staged.Journal))).IsTrue();
    }
}
