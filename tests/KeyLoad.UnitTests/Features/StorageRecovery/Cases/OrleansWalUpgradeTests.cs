using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OrleansWalUpgradeTests
{
    private const long PreservedReadGeneration = 7;

    [Test]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(4, false)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(3, true)]
    [Arguments(4, true)]
    public async Task AcIs007LegacyEmptyOrCompactedSourcesRefuseReinterpretationWithoutChangingAnyFile(
        int version, bool checkpoint)
    {
        using var files = new WalFileFixture();
        StoreIdentity original;
        using (var store = new ZoneTreeStore(new(files.DirectoryPath)))
        {
            if (checkpoint)
            {
                store.Commit((transaction, _) => { transaction.Put([0x00, 0xFF], [0x80, 0x00]); return true; });
                store.Compact();
            }
            original = store.Identity with
            {
                FormatVersion = version,
                ReadGeneration = PreservedReadGeneration,
                DispatchPaused = true
            };
        }
        await WriteIdentity(files, original);
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged(await files.ReadJournalAsync(),
            await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task AcWal003And004CompleteLegacyJsonJournalIsRefusedWithoutChangingAuthoritativeFiles(int version)
    {
        using var files = new WalFileFixture();
        var original = files.Initialize() with { FormatVersion = version };
        await WriteIdentity(files, original);
        var payload = JsonDefaults.Serialize(new StorageMutation[] { new(new byte[] { 0x10 }, new byte[] { 0x30 }) });
        var journal = WalFileFixture.CreateFrame(payload, magic: WalFileFixture.LegacyMagic);
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task AcWal004LegacyIdentityCannotAcceptFullBinaryJournalOrPromoteBeforeRecovery(int version)
    {
        using var files = new WalFileFixture();
        var original = files.Initialize() with { FormatVersion = version };
        await WriteIdentity(files, original);
        using var serializer = new WalSerializerFixture();
        var payload = serializer.Serialize([new() { Key = new byte[] { 0x10 }, Value = new byte[] { 0x30 }, Kind = ZoneTreeJournalMutation.PutKind }]);
        var journal = WalFileFixture.CreateFrame(payload);
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task AcWal004LegacyJsonAfterVerifiedCheckpointRefusesUpgradeWithoutRewritingJournal(int version)
    {
        using var files = new WalFileFixture();
        StoreIdentity identity;
        using (var store = new ZoneTreeStore(new(files.DirectoryPath)))
        {
            store.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
            store.Compact();
            identity = store.Identity with { FormatVersion = version };
        }
        await WriteIdentity(files, identity);
        var payload = JsonDefaults.Serialize(new StorageMutation[] { new(new byte[] { 0x20 }, new byte[] { 0x40 }) });
        await using (var output = new FileStream(files.JournalPath, FileMode.Append, FileAccess.Write))
        {
            await output.WriteAsync(WalFileFixture.CreateFrame(payload, sequence: 2, magic: WalFileFixture.LegacyMagic));
        }
        var journal = await files.ReadJournalAsync();
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task AcIs007OldIdentityRefusesReinterpretationAndCurrentCheckpointCorruptionStillFails(int version)
    {
        using var files = new WalFileFixture();
        StoreIdentity identity;
        using (var store = new ZoneTreeStore(new(files.DirectoryPath)))
        {
            store.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
            store.Compact();
            identity = store.Identity with { FormatVersion = version };
        }
        await WriteIdentity(files, identity);
        var journal = await files.ReadJournalAsync();
        journal[^1] ^= 1;
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), version == WalFileFixture.CurrentIdentityVersion ? ErrorCode.Corruption : ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
    }

    [Test]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(3, true)]
    [Arguments(4, true)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(4, false)]
    [Arguments(5, true)]
    [Arguments(5, false)]
    [Arguments(6, true)]
    [Arguments(6, false)]
    public async Task AcWal003And004FullLegacyHeaderWithMissingOrTornPayloadFailsWithoutTruncation(
        int version, bool missingPayload)
    {
        using var files = new WalFileFixture();
        await WriteIdentity(files, files.Initialize() with { FormatVersion = version });
        var payload = JsonDefaults.Serialize(new StorageMutation[]
        {
            new(new byte[] { 0x10 }, new byte[] { 0x30 })
        });
        var complete = WalFileFixture.CreateFrame(payload, magic: WalFileFixture.LegacyMagic);
        var journal = missingPayload ? complete[..WalFileFixture.HeaderBytes] : complete[..^1];
        await File.WriteAllBytesAsync(files.JournalPath, journal);

        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
    }

    [Test]
    [Arguments(0)]
    [Arguments(7)]
    public async Task AcWal004UnsupportedIdentityVersionFailsBeforeJournalMutation(int version)
    {
        using var files = new WalFileFixture();
        var identity = files.Initialize() with { FormatVersion = version };
        await files.WriteIdentityAsync(identity);
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged([], await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
    }
    private static Task WriteIdentity(WalFileFixture files, StoreIdentity identity)
        => identity.FormatVersion == WalFileFixture.CurrentIdentityVersion
            ? files.WriteIdentityAsync(identity)
            : files.WriteLegacyJsonIdentityAsync(identity);
}
