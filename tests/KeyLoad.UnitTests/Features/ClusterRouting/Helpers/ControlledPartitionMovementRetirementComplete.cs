using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Completes retirement only with the actual ACKed source terminal journal and original exact cleanup body.</summary>
internal static class ControlledPartitionMovementRetirementComplete
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;
    private static readonly Guid CommandId = Guid.Parse("6f966563-36d3-4a4b-9287-516a9e4d48d0");

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult finalized,
        PartitionMovePhaseResult terminal, Guid originalGrantId, ReadOnlyMemory<byte> originalCleanupBody,
        string callerAddress, DateTimeOffset wholeExpiresAt, CancellationToken cancellationToken)
    {
        var control = finalized.Control ?? throw new InvalidOperationException("Actual Published control is absent.");
        var body = NativeSerialization.Serialize(new PartitionMoveCompletionBody(
            PhysicalShardCatalogFixture.RootPrincipalId, control, terminal.Journal, null,
            originalGrantId, null, originalCleanupBody, ReadOnlyMemory<byte>.Empty));
        var expiry = ControlledPartitionMovementFirstPhaseExpiry.Create(source, runtime, wholeExpiresAt, cancellationToken);
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            finalized.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlCompleteRetirement,
            ControlOrdinal, expiry, Guid.NewGuid(), body);
        var outcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(CommandId, envelope, null, corpus.Control.Owner.VoterIds.First(), callerAddress,
                PartitionMovementTransportAction.Apply, Guid.Empty, ControlOrdinal), cancellationToken);
        await Assert.That(outcome.Error).IsNull();
        var actual = outcome.Get<PartitionMovePhaseResult>();
        var expected = control with { Phase = PartitionMovePhase.Retired };
        await Assert.That(actual.Stage).IsEqualTo(PartitionMovePeerStage.ControlCompleteRetirement);
        await Assert.That(actual.Journal.CommandId).IsEqualTo(CommandId);
        await Assert.That(JsonDefaults.Serialize(actual.Control).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(actual.PublishedPlacement)
            .SequenceEqual(JsonDefaults.Serialize(control.PublishedPlacement))).IsTrue();
        await Assert.That(actual.Fence).IsNull();
        await Assert.That(actual.InstalledReceipt).IsNull();
        return actual;
    }
}
