using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks complete real abort progress against independently bounded family and original journal expectations.</summary>
internal static class ControlledPartitionMovementAbortStateAssertions
{
    private const int Version = 1;
    private const int SequenceStep = 1;

    internal static async Task AssertAsync(PartitionMovePhaseResult actual, PartitionMoveCleanupRole role,
        int familyOrdinal, int terminalOrdinal, string originalDigest)
    {
        await Assert.That(actual.Stage).IsEqualTo(PartitionMovePeerStage.Abort);
        var expected = new PartitionMoveCleanupState(Version,
            ControlledPartitionMovementPrepareRequest.MoveId, ControlledPartitionMovementCorpus.Partition,
            originalDigest, PartitionMovePeerStage.Abort, role,
            familyOrdinal == terminalOrdinal ? terminalOrdinal : checked(familyOrdinal + SequenceStep),
            familyOrdinal == terminalOrdinal ? actual.Journal : null, checked(familyOrdinal + SequenceStep));
        await Assert.That(JsonDefaults.Serialize(actual.Cleanup)
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Control).IsNull();
        await Assert.That(actual.Fence).IsNull();
        await Assert.That(actual.InstalledReceipt).IsNull();
        await Assert.That(actual.PublishedPlacement).IsNull();
    }
}
