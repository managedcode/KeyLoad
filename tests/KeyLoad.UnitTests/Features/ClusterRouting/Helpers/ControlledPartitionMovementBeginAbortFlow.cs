using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Begins abort only from the exact actual A control while the original capture remains owned.</summary>
internal static class ControlledPartitionMovementBeginAbortFlow
{
    private const int Version = 1;
    private const int InitialOrdinal = 0;
    private static readonly Guid CommandId = Guid.Parse("8b337be6-05aa-4421-b35c-62cda81bca88");

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult acceptedFence,
        string originalCallerAddress, DateTimeOffset originalExpiry, CancellationToken cancellationToken)
    {
        var originalControl = acceptedFence.Control
            ?? throw new InvalidOperationException("The actual original Fenced control is absent.");
        var body = NativeSerialization.Serialize(new PartitionMoveControlBody(
            PhysicalShardCatalogFixture.RootPrincipalId, originalControl));
        var envelope = new PartitionMovePeerEnvelope(Version, originalControl.MoveId,
            originalControl.Partition, corpus.Control.Owner, originalControl.SourcePlacement,
            corpus.Destination.Owner, acceptedFence.Journal.ControlIntentDigest,
            PartitionMovePeerStage.ControlBeginAbort, InitialOrdinal, originalExpiry, Guid.NewGuid(), body);
        var outcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(CommandId, envelope, null, corpus.Control.Owner.VoterIds.First(), originalCallerAddress,
                PartitionMovementTransportAction.Apply, Guid.Empty, InitialOrdinal), cancellationToken);
        await Assert.That(outcome.Error).IsNull();
        var actual = outcome.Get<PartitionMovePhaseResult>();
        var expected = originalControl with { Phase = PartitionMovePhase.Aborting };
        await Assert.That(JsonDefaults.Serialize(actual.Control)
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Journal.CommandId).IsEqualTo(CommandId);
        await Assert.That(actual.Fence).IsNull();
        await Assert.That(actual.InstalledReceipt).IsNull();
        await Assert.That(actual.PublishedPlacement).IsNull();
        return actual;
    }
}
