using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Publishes protected control lineage through the actual installed control and local A journal.</summary>
internal static class ControlledPartitionMovementFinalizeFlow
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;
    private const long FirstDirectoryRevision = 1;
    private const long PublishedOwnershipEpoch = 2;
    private static readonly Guid CommandId = Guid.Parse("d59f6eec-ae14-45ba-91a5-d608219887c0");

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult actualInstalled,
        string callerAddress, DateTimeOffset expiry, CancellationToken cancellationToken)
    {
        var control = actualInstalled.Control
            ?? throw new InvalidOperationException("Actual Installed control is absent.");
        var body = NativeSerialization.Serialize(new PartitionMoveControlBody(
            PhysicalShardCatalogFixture.RootPrincipalId, control));
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            actualInstalled.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlFinalize,
            ControlOrdinal, expiry, Guid.NewGuid(), body);
        var outcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(CommandId, envelope, null, corpus.Control.Owner.VoterIds.First(), callerAddress,
                PartitionMovementTransportAction.Apply, Guid.Empty, ControlOrdinal), cancellationToken);
        await Assert.That(outcome.Error).IsNull();
        var actual = outcome.Get<PartitionMovePhaseResult>();
        await Assert.That(actual.Stage).IsEqualTo(PartitionMovePeerStage.ControlFinalize);
        await Assert.That(actual.Journal.CommandId).IsEqualTo(CommandId);
        await Assert.That(actual.PublishedPlacement).IsNotNull();
        var expectedRow = new AtomicPartitionPlacementV1(Version, control.Partition,
            corpus.Destination.Owner.PhysicalShardId, FirstDirectoryRevision,
            corpus.Destination.Owner.Incarnation, corpus.Destination.Owner.VoterIds, PublishedOwnershipEpoch);
        var expected = control with { Phase = PartitionMovePhase.Published, PublishedPlacement = expectedRow };
        await Assert.That(JsonDefaults.Serialize(actual.Control)
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(actual.PublishedPlacement)
            .SequenceEqual(JsonDefaults.Serialize(expectedRow))).IsTrue();
        await Assert.That(actual.Fence).IsNull();
        await Assert.That(actual.InstalledReceipt).IsNull();
        return actual;
    }
}
