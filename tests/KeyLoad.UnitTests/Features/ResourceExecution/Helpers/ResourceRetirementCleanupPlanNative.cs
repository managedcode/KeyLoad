using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementCleanupNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Checks real table planning and native candidate application without synthetic D3 authority.</summary>
internal static class ResourceRetirementCleanupPlanNative
{
    internal static async Task RunAsync(TestDatabase fixture)
    {
        var state = ResourceRetirementCleanupNativeSeed.Create(fixture);
        var partition = state.Partitions[FirstIndex];
        var image = ResourceRetirementCleanupNativeImages.Read(fixture.Store);
        var position = fixture.Store.Position;
        var limits = fixture.Database.Limits with
        {
            MaxBatchMutations = RowsPerPartition,
            MaxScanRecords = RowsPerPartition
        };
        var expected = image.Where(row => row.Key.StartsWith(Convert.ToHexString(
            KeySpace.Partition(DocumentFamily, partition, Table)), StringComparison.Ordinal))
            .OrderBy(row => row.Key, StringComparer.Ordinal).ToArray();
        ResourceRetirementTableCleanupSelection? selected = null;
        ResourceRetirementTableCleanupSelection? maximal = null;
        KeyLoadException? rejected = null;
        fixture.Store.Commit((transaction, _) =>
        {
            var plan = ResourceRetirementTableCleanupPage.Plan(transaction, state.Lifetime, partition,
                new(Zero), Options.Create(limits),
                (limits.MaxQueryReadBytes, limits.MaxScanRecords), Zero, Zero, stopAtCapacity: true);
            maximal = plan.Select(plan.Count);
            selected = plan.Select(One);
            rejected = Assert.ThrowsExactly<KeyLoadException>(() => plan.Select(RowsPerPartition + One));
            ((IAtomicCommitCandidateValidation)transaction).ValidateCandidate(selected.Deletes);
            return true;
        });
        await Assert.That(rejected!.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await ResourceRetirementCleanupNativeImages.SameAsync(fixture.Store, image, position);
        await Assert.That(maximal!.Progress.DeletedRecords).IsEqualTo(RowsPerPartition);
        await Assert.That(selected!.Progress.DeletedRecords).IsEqualTo(One);
        var examined = expected.Sum(row => (long)(row.Key.Length + row.Value.Length) / HexCharactersPerByte);
        await Assert.That(maximal.Progress.ExaminedBytes).IsEqualTo(examined);
        await Assert.That(selected.Progress.ExaminedBytes).IsEqualTo(examined);
        await Assert.That(selected.Progress.ExaminedRecords).IsEqualTo(RowsPerPartition);
        await Assert.That(Convert.ToHexString(selected.Progress.Next.AfterKey)).IsEqualTo(expected[FirstIndex].Key);
        await Assert.That(selected.Progress.HasMore).IsTrue();
        await Assert.That(selected.Progress.DeletedBytes).IsEqualTo((long)(expected[FirstIndex].Key.Length
            + expected[FirstIndex].Value.Length) / HexCharactersPerByte);
        var rolledBack = Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Store.Commit<bool>((transaction, _) =>
        {
            selected.Apply(transaction);
            transaction.Put(Checkpoint(partition), KeyCodec.Encode(selected.Progress.Next.FamilyOrdinal, selected.Progress.Next.AfterKey));
            throw new InvalidOperationException(Rollback);
        }));
        await Assert.That(rolledBack!.Message).IsEqualTo(Rollback);
        await ResourceRetirementCleanupNativeImages.SameAsync(fixture.Store, image, position);
        fixture.Store.Commit((transaction, _) => { selected.Apply(transaction); return true; });
        image.Remove(expected[FirstIndex].Key);
        await ResourceRetirementCleanupNativeImages.SameAsync(fixture.Store, image, position + One);
    }
}
