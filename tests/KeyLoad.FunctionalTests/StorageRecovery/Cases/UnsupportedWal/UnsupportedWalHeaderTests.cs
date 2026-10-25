namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class UnsupportedWalHeaderTests
{
    private const ulong UnsupportedJournalMagic = ulong.MaxValue;
    private const byte FirstPayloadByte = 0x30;

    [Test]
    public async Task UnsupportedJournalMagicOnCurrentIdentityIsRejectedWithoutTruncation()
    {
        using var files = new WalFileFixture();
        var originalIdentity = files.Initialize();
        var originalJournal = await files.ReadJournalAsync();
        var originalIdentityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var journal = WalFileFixture.CreateFrame([FirstPayloadByte], magic: UnsupportedJournalMagic);
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        var before = await files.CaptureFilesAsync();

        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
        await CurrentWalFrameRepairContinuation.RequireAsync(files, originalIdentity, originalJournal,
            originalIdentityBytes, TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
    }
}
