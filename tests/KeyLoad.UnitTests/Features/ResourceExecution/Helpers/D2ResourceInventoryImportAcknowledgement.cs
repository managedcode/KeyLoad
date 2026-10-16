using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryImportAcknowledgement
{
    internal static async Task ExecuteAsync(D2ResourceInventoryImportOwners owners,
        D2ResourceInventoryImportImage image, PartitionMovePhaseResult installed, int ordinal, CancellationToken token)
    {
        var control = image.Captured.Control ?? throw new InvalidOperationException("The genuine Captured control is missing.");
        var grantId = ControlledPartitionMovementTargetPhaseIds.InstallGrant(ordinal);
        var expiry = ControlledPartitionMovementFirstPhaseExpiry.Create(owners.Source, owners.SourceRuntime,
            image.Handle.ExpiresAt, token);
        var body = NativeSerialization.Serialize(new PartitionMoveAcknowledgeBody(grantId, installed.Journal));
        var envelope = new PartitionMovePeerEnvelope(D2ResourceInventoryImportData.Version,
            control.MoveId, control.Partition, owners.Corpus.Control.Owner, control.SourcePlacement,
            owners.Corpus.Destination.Owner, image.Captured.Journal.ControlIntentDigest,
            PartitionMovePeerStage.ControlAcknowledge, D2ResourceInventoryImportData.Empty, expiry, Guid.NewGuid(), body);
        var request = new PartitionMovementTransportRequest(
            ControlledPartitionMovementTargetPhaseIds.InstallAcknowledgement(ordinal), envelope, null,
            owners.Corpus.Control.Owner.VoterIds.First(), owners.CallerAddress, PartitionMovementTransportAction.Apply,
            Guid.Empty, D2ResourceInventoryImportData.Empty);
        var result = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(owners.Source,
            owners.SourceRuntime, owners.SourceAdmission, request, token);
        await Assert.That(result.Error).IsNull();
        var grant = result.Get<PartitionMovePhaseResult>().Grant
            ?? throw new InvalidOperationException("The actual Install acknowledgement grant is missing.");
        await Assert.That(grant.GrantId).IsEqualTo(grantId);
        await D2ResourceInventoryAssertions.EqualAsync(installed.Journal, grant.Settlement);
    }
}
