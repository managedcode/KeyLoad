using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class CurrentWalFrameRepairContinuation
{
    internal static async Task RequireAsync(WalFileFixture files, StoreIdentity originalIdentity,
        byte[] originalJournal, byte[] originalIdentityBytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await File.WriteAllBytesAsync(files.JournalPath, originalJournal, token);
        await Assert.That(await files.ReadJournalAsync())
            .IsEquivalentTo(originalJournal, CollectionOrdering.Matching);
        await RequireCurrentAsync(files, originalIdentity, originalIdentityBytes, token);
    }

    internal static async Task RequireCurrentAsync(WalFileFixture files, StoreIdentity originalIdentity,
        byte[] originalIdentityBytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath, token))
            .IsEquivalentTo(originalIdentityBytes, CollectionOrdering.Matching);
        using (var repaired = new ZoneTreeStore(new(files.DirectoryPath),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            await WalFileFixture.AssertPreservedIdentity(originalIdentity, repaired.Identity);
            await Assert.That(repaired.Identity.MinimumReaderContract)
                .IsEqualTo(originalIdentity.MinimumReaderContract);
            await Assert.That(repaired.Position).IsEqualTo(0L);
            var empty = repaired.Read(view => view.Scan([], 1));
            await Assert.That(empty.HasMore).IsFalse();
            await Assert.That(empty.Records.Length).IsEqualTo(0);
            token.ThrowIfCancellationRequested();
            repaired.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
            await Assert.That(repaired.Position).IsEqualTo(1L);
        }
        await CurrentCheckpointRepairContinuation.RequireCurrentAsync(files, originalIdentity, 1L, token);
    }
}
