using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Advances Installed only through the actual ACKed terminal B receipt and original returned descriptor.</summary>
internal static class ControlledPartitionMovementInstalledAdvance
{
    private const int Version = 1;
    private const int InitialOrdinal = 0;
    private static readonly Guid CommandId = Guid.Parse("302cce47-83b8-4f34-a0b1-d2358282015b");

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult acceptedFence,
        PartitionMoveImageDescriptor originalDescriptor, PartitionMovePhaseResult actualTerminalInstall,
        int originalPageCount, string callerAddress, DateTimeOffset originalExpiry,
        CancellationToken cancellationToken)
    {
        var control = acceptedFence.Control
            ?? throw new InvalidOperationException("The actual Fenced control is required.");
        var fence = acceptedFence.Fence
            ?? throw new InvalidOperationException("The original source fence is required.");
        var receipt = actualTerminalInstall.InstalledReceipt
            ?? throw new InvalidOperationException("The actual terminal Install receipt is required.");
        var body = NativeSerialization.Serialize(new PartitionMoveAdvanceBody(
            PhysicalShardCatalogFixture.RootPrincipalId, control, fence, originalDescriptor,
            receipt, SourceCaptureGrantId: null,
            TargetInstallGrantId: ControlledPartitionMovementTargetPhaseIds.InstallGrant(originalPageCount)));
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            acceptedFence.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlAdvance,
            InitialOrdinal, originalExpiry, Guid.NewGuid(), body);
        var result = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(CommandId, envelope, null, corpus.Control.Owner.VoterIds.First(), callerAddress,
                PartitionMovementTransportAction.Apply, Guid.Empty, InitialOrdinal), cancellationToken);
        await Assert.That(result.Error).IsNull();
        var actual = result.Get<PartitionMovePhaseResult>();
        var expected = control with { Phase = PartitionMovePhase.Installed, InstalledReceipt = receipt.Token };
        await Assert.That(JsonDefaults.Serialize(actual.Control).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(actual.Fence).SequenceEqual(JsonDefaults.Serialize(fence))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(actual.InstalledReceipt)
            .SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await Assert.That(actual.PublishedPlacement).IsNull();
        return actual;
    }
}
