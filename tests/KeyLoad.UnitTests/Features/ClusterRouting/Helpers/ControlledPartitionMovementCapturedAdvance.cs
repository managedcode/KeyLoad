using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Publishes Captured only through the actual settled A capture grant and original returned descriptor.</summary>
internal static class ControlledPartitionMovementCapturedAdvance
{
    private const int Version = 1;
    private const int InitialOrdinal = 0;
    private static readonly Guid CommandId = Guid.Parse("fbb9a625-cfc9-49fc-bebc-00a32e06fbf4");

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult acceptedFence,
        PartitionMoveImageDescriptor originalDescriptor, string callerAddress, DateTimeOffset originalExpiry,
        CancellationToken cancellationToken)
    {
        var control = acceptedFence.Control
            ?? throw new InvalidOperationException("The actual Fenced control is required.");
        var fence = acceptedFence.Fence
            ?? throw new InvalidOperationException("The original source fence is required.");
        var body = NativeSerialization.Serialize(new PartitionMoveAdvanceBody(
            PhysicalShardCatalogFixture.RootPrincipalId, control, fence, originalDescriptor,
            null, ControlledPartitionMovementCaptureRequest.GrantId));
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            acceptedFence.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlAdvance,
            InitialOrdinal, originalExpiry, Guid.NewGuid(), body);
        var result = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(CommandId, envelope, null, corpus.Control.Owner.VoterIds.First(), callerAddress,
                PartitionMovementTransportAction.Apply, Guid.Empty, InitialOrdinal), cancellationToken);
        await Assert.That(result.Error).IsNull();
        var actual = result.Get<PartitionMovePhaseResult>();
        var expected = control with { Phase = PartitionMovePhase.Captured, ImageDigest = originalDescriptor.Digest };
        await Assert.That(JsonDefaults.Serialize(actual.Control).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(actual.Fence).SequenceEqual(JsonDefaults.Serialize(fence))).IsTrue();
        await Assert.That(actual.InstalledReceipt).IsNull();
        await Assert.That(actual.PublishedPlacement).IsNull();
        return actual;
    }
}
