using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class CurrentWalRejectionTests
{
    private const int ChangedByte = 1;
    private const int UnsupportedIdentityVersion = 8;

    [Test]
    public async Task UnsupportedIdentityVersionFailsBeforeJournalMutation()
    {
        using var files = new WalFileFixture();
        var identity = files.Initialize() with { FormatVersion = UnsupportedIdentityVersion };
        await files.WriteIdentityAsync(identity);
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged([], await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
    }

    [Test]
    public async Task CorruptCurrentCheckpointFailsWithoutChangingAnyStoreFile()
    {
        using var files = new WalFileFixture();
        using (var store = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            store.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
            store.Compact();
        }
        var journal = await files.ReadJournalAsync();
        journal[^ChangedByte] ^= ChangedByte;
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.Corruption);
        await files.AssertFilesUnchangedAsync(before);
    }
}
