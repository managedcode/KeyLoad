using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

/// <summary>Accounts for the complete unchanged terminal trial and every appended Blob suffix phase.</summary>
internal static class ControlledBlobOutcomeLifetimeNativeHistory
{
    private const long MaximumTransferPages = 55;
    private const long TerminalFixedSourceEntries = 15 + 4 + 2 + 1 + 1 + 2 + 1;
    private const long SourceEntriesPerPage = 4;
    private const long TerminalFixedTargetEntries = 3;
    private const long TargetEntriesPerPage = 2;
    private const long SeedOperations = 1 + 2 + 3 + 2 + 4 + 3;
    private const long HealthyContinuationOperations = 1;
    private const long SourcePhasesPerOperation = 5;
    private const long TargetPhasesPerOperation = 1;
    private const long SourcePolicyEntries = 3;
    private const long TargetPolicyEntries = 2;
    private const long SourceSuffixEntries = (SeedOperations + HealthyContinuationOperations)
        * SourcePhasesPerOperation + SourcePolicyEntries;
    private const long TargetSuffixEntries = (SeedOperations + HealthyContinuationOperations)
        * TargetPhasesPerOperation + TargetPolicyEntries;

    internal static async Task BoundsAsync()
    {
        var terminalSource = TerminalFixedSourceEntries
            + PartitionMovementCleanupCountAssertions.CleanupBatchCount
            * PartitionMovementCleanupCountAssertions.RetireSourceCommits
            + SourceEntriesPerPage * MaximumTransferPages;
        var terminalTarget = TerminalFixedTargetEntries + TargetEntriesPerPage * MaximumTransferPages;
        await Assert.That(checked(terminalSource + SourceSuffixEntries))
            .IsEqualTo(ControlledPartitionMovementNativeJournal.BlobOutcomeSourceHistoryEntries);
        await Assert.That(checked(terminalTarget + TargetSuffixEntries))
            .IsEqualTo(ControlledPartitionMovementNativeJournal.BlobOutcomeTargetHistoryEntries);
    }

    internal static async Task CompleteAsync(ControlledBlobOutcomeLifetimeNativeState state,
        long sourceIndex, long targetIndex)
    {
        await Assert.That(state.Source.Journal.Log.State.LastIndex)
            .IsEqualTo(checked(sourceIndex + SourceSuffixEntries));
        await Assert.That(state.Target.Journal.Log.State.LastIndex)
            .IsEqualTo(checked(targetIndex + TargetSuffixEntries));
    }
}
