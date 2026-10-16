using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementCleanupNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementCleanupNativeRefusals
{
    internal static async Task RunAsync(TestDatabase fixture, ResourceRetirementCleanupNativeState state, DatabaseLimits limits)
    {
        var before = ResourceRetirementCleanupNativeImages.Read(fixture.Store);
        var position = fixture.Store.Position;
        var partition = state.Partitions[FirstIndex];
        var key = KeySpace.Partition(DocumentFamily, partition, Table, First);
        await RefuseAsync(fixture, state, limits with
        {
            MaxBatchMutations = ReservedMutations
        }, before, position);
        await RefuseAsync(fixture, state, limits with
        {
            MaxBatchBytes = checked((int)(ReservedBytes + key.Length * CursorKeyCopies - One))
        }, before, position);
        await RefuseAsync(fixture, state, limits with
        {
            MaxQueryReadBytes = One
        }, before, position);
        var stale = new ResourceRetirementTableCleanupCursor(Zero,
            KeySpace.Partition(DocumentFamily, state.Partitions[SecondIndex], Table, First));
        var invalid = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Commit((transaction, _) =>
            ResourceRetirementTableCleanupPage.Apply(transaction, state.Lifetime, partition, stale,
                Options.Create(limits), (limits.MaxQueryReadBytes, limits.MaxScanRecords),
                ReservedMutations, ReservedBytes)));
        await Assert.That(invalid!.Code).IsEqualTo(ErrorCode.Corruption);
        await ResourceRetirementCleanupNativeImages.SameAsync(fixture.Store, before, position);
        var rolledBack = Assert.ThrowsExactly<InvalidOperationException>(() =>
            ResourceRetirementCleanupNativePages.Commit(fixture.Store, state, partition, limits, rollback: true));
        await Assert.That(rolledBack!.Message).IsEqualTo(Rollback);
        await ResourceRetirementCleanupNativeImages.SameAsync(fixture.Store, before, position);
    }

    private static async Task RefuseAsync(TestDatabase fixture, ResourceRetirementCleanupNativeState state,
        DatabaseLimits limits, Dictionary<string, string> before, long position)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => ResourceRetirementCleanupNativePages.Commit(
            fixture.Store, state, state.Partitions[FirstIndex], limits));
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await ResourceRetirementCleanupNativeImages.SameAsync(fixture.Store, before, position);
    }
}
