using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks every bounded retirement progress field against the original literal one-page-per-family model.</summary>
internal static class ControlledPartitionMovementRetireStateAssertions
{
    private const int Version = 1;
    private const int SequenceStep = 1;

    internal static async Task AssertAsync(PartitionMoveCleanupState actual,
        PartitionMoveJournalReceipt originalJournal, string originalIntentDigest,
        int familyOrdinal, int terminalOrdinal)
    {
        var terminal = familyOrdinal == terminalOrdinal;
        var expected = new PartitionMoveCleanupState(Version,
            ControlledPartitionMovementPrepareRequest.MoveId, ControlledPartitionMovementCorpus.Partition,
            originalIntentDigest, PartitionMovePeerStage.Retire, PartitionMoveCleanupRole.Source,
            terminal ? terminalOrdinal : checked(familyOrdinal + SequenceStep),
            terminal ? originalJournal : null, checked(familyOrdinal + SequenceStep));
        await Assert.That(JsonDefaults.Serialize(actual)
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
