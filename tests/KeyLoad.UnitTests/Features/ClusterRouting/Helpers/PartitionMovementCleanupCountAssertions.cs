using KeyLoad.Core.Features.ClusterRouting.Execution;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PartitionMovementCleanupCountAssertions
{
    private const string BlobState = "blob-state-v1";
    private const string BlobHead = "blob-head-v1";
    private const string TargetPage = "partition-move-target-page-v1";
    private const int CleanupFamilyCount = 69;
    private const long RetireSourceCommits = 3;
    private const long AbortSourceCommits = 2;
    private const long AbortTargetCommits = 1;

    internal static async Task InventoryAsync()
    {
        await Assert.That(PartitionRecordInventoryTests.ExpectedFamilies.Length)
            .IsEqualTo(PartitionRecordInventoryTests.ExpectedFamilyCount);
        var expected = PartitionRecordInventoryTests.ExpectedFamilies
            .Where(family => family != BlobState && family != BlobHead)
            .Prepend(BlobState).Append(BlobHead).Append(TargetPage).ToArray();
        await Assert.That(expected.Length).IsEqualTo(CleanupFamilyCount);
        await Assert.That(PartitionMoveCleanupFamilies.All).IsEquivalentTo(expected,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task RetireAsync(ControlledPartitionMovementNode source, long originalIndex)
        => await Assert.That(source.Journal.Log.State.LastIndex)
            .IsEqualTo(checked(originalIndex + RetireSourceCommits));

    internal static async Task AbortAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, long sourceIndex, long targetIndex)
    {
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(checked(sourceIndex + AbortSourceCommits));
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(checked(targetIndex + AbortTargetCommits));
    }
}
