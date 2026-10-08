using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks the complete terminal receipt against independently counted original target operations.</summary>
internal static class ControlledPartitionMovementTargetInstallReceipt
{
    private const int MaximumTransferFamilies = 55;
    private const long TargetBootstrapEntries = 1;
    private const long PageStages = 2;
    private const long TerminalEntries = 1;
    private const long OriginalEpoch = 1;

    internal static async Task AssertAsync(ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult terminal, int pageCount)
    {
        await Assert.That(pageCount <= MaximumTransferFamilies).IsTrue();
        var expectedIndex = checked(TargetBootstrapEntries + PageStages * pageCount + TerminalEntries);
        var expected = new CommitReceipt(ControlledPartitionMovementTargetPhaseIds.InstallCommand(pageCount),
            new(corpus.Destination.Owner.Incarnation, ControlledPartitionMovementCorpus.Partition.AtomicPartitionId,
                expectedIndex, OriginalEpoch), [], DurabilityProfile.ProcessDurable);
        await Assert.That(JsonDefaults.Serialize(terminal.InstalledReceipt)
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(expectedIndex);
        await Assert.That(target.Journal.Log.State.CommittedIndex).IsEqualTo(expectedIndex);
    }
}
