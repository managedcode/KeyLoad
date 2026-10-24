using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class CurrentWalRejectionTests
{
    private const int ChangedByte = 1;
    private const int UnsupportedIdentityVersion = 8;

    [Test]
    public async Task UnsupportedIdentityVersionFailsBeforeJournalMutation()
    {
        using var files = new WalFileFixture();
        var originalIdentity = files.Initialize();
        long originalPosition;
        using (var store = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            store.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
            originalIdentity = store.Identity;
            originalPosition = store.Position;
        }
        var token = TestContext.Current!.Execution.CancellationToken;
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath, token);
        var originalJournal = await files.ReadJournalAsync();
        await files.WriteIdentityAsync(originalIdentity with
        {
            FormatVersion = UnsupportedIdentityVersion
        });
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged(originalJournal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
        await File.WriteAllBytesAsync(files.IdentityPath, identityBytes, token);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath, token))
            .IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await CurrentCheckpointRepairContinuation.RequireCurrentAsync(files, originalIdentity, originalPosition,
            token).ConfigureAwait(false);
    }

    [Test]
    public async Task CorruptCurrentCheckpointFailsWithoutChangingAnyStoreFile()
    {
        using var files = new WalFileFixture();
        StoreIdentity originalIdentity;
        long originalPosition;
        using (var store = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            store.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
            store.Compact();
            originalIdentity = store.Identity;
            originalPosition = store.Position;
        }
        var journal = await files.ReadJournalAsync();
        var originalJournal = journal.ToArray();
        journal[^ChangedByte] ^= ChangedByte;
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.Corruption);
        await files.AssertFilesUnchangedAsync(before);
        await CurrentCheckpointRepairContinuation.RequireAsync(files, originalIdentity, originalPosition,
            originalJournal, TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
    }
}
