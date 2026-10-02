using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OrleansWalUpgradeTests
{
    private const long PreservedReadGeneration = 7;

    [Test]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    public async Task AcWal004LegacyEmptyOrVerifiedCheckpointPromotesIdentityWithoutChangingScope(
        int version, bool checkpoint)
    {
        using var files = new WalFileFixture();
        StoreIdentity original;
        long expectedPosition;
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
            expectedPosition = store.Position;
        }
        await files.WriteIdentityAsync(original);
        files.RemoveMaterializedTree();
        var journal = await files.ReadJournalAsync();

        using (var reopened = new ZoneTreeStore(new(files.DirectoryPath)))
        {
            await WalFileFixture.AssertPreservedIdentity(original, reopened.Identity);
            await WalFileFixture.AssertPreservedIdentity(original, await files.ReadIdentityAsync());
            await Assert.That(reopened.Position).IsEqualTo(expectedPosition);
            if (checkpoint)
            {
                await Assert.That(reopened.Read(view => view.ReadOwnedValue([0x00, 0xFF])))
                    .IsEquivalentTo(new byte[] { 0x80, 0x00 }, CollectionOrdering.Matching);
            }
            await Assert.That(await files.ReadJournalAsync()).IsEquivalentTo(journal, CollectionOrdering.Matching);
            reopened.Commit((transaction, _) => { transaction.Put([0x20], []); return true; });
        }
        using var current = new ZoneTreeStore(new(files.DirectoryPath));
        await WalFileFixture.AssertPreservedIdentity(original, current.Identity);
        await Assert.That(current.Position).IsEqualTo(expectedPosition + 1);
        await Assert.That(current.Read(view => view.ReadOwnedValue([0x20]))).IsNotNull();
        await Assert.That(current.Read(view => view.ReadOwnedValue([0x20]))).IsEmpty();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task AcWal003And004CompleteLegacyJsonJournalIsRefusedWithoutChangingAuthoritativeFiles(int version)
    {
        using var files = new WalFileFixture();
        var original = files.Initialize() with { FormatVersion = version };
        await files.WriteIdentityAsync(original);
        var payload = JsonDefaults.Serialize(new StorageMutation[] { new(new byte[] { 0x10 }, new byte[] { 0x30 }) });
        var journal = WalFileFixture.CreateFrame(payload, magic: WalFileFixture.LegacyMagic);
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task AcWal004LegacyIdentityCannotAcceptFullBinaryJournalOrPromoteBeforeRecovery(int version)
    {
        using var files = new WalFileFixture();
        var original = files.Initialize() with { FormatVersion = version };
        await files.WriteIdentityAsync(original);
        using var serializer = new WalSerializerFixture();
        var payload = serializer.Serialize([new() { Key = new byte[] { 0x10 }, Value = new byte[] { 0x30 }, Kind = ZoneTreeJournalMutation.PutKind }]);
        var journal = WalFileFixture.CreateFrame(payload);
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
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
        await files.WriteIdentityAsync(identity);
        var payload = JsonDefaults.Serialize(new StorageMutation[] { new(new byte[] { 0x20 }, new byte[] { 0x40 }) });
        await using (var output = new FileStream(files.JournalPath, FileMode.Append, FileAccess.Write))
        {
            await output.WriteAsync(WalFileFixture.CreateFrame(payload, sequence: 2, magic: WalFileFixture.LegacyMagic));
        }
        var journal = await files.ReadJournalAsync();
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task AcWal004InvalidLegacyCheckpointFailsWithoutPromotingIdentity(int version)
    {
        using var files = new WalFileFixture();
        StoreIdentity identity;
        using (var store = new ZoneTreeStore(new(files.DirectoryPath)))
        {
            store.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
            store.Compact();
            identity = store.Identity with { FormatVersion = version };
        }
        await files.WriteIdentityAsync(identity);
        var journal = await files.ReadJournalAsync();
        journal[^1] ^= 1;
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.Corruption);
    }

    [Test]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(3, true)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    public async Task AcWal003And004FullLegacyHeaderWithMissingOrTornPayloadFailsWithoutTruncation(
        int version, bool missingPayload)
    {
        using var files = new WalFileFixture();
        await files.WriteIdentityAsync(files.Initialize() with { FormatVersion = version });
        var payload = JsonDefaults.Serialize(new StorageMutation[]
        {
            new(new byte[] { 0x10 }, new byte[] { 0x30 })
        });
        var complete = WalFileFixture.CreateFrame(payload, magic: WalFileFixture.LegacyMagic);
        var journal = missingPayload ? complete[..WalFileFixture.HeaderBytes] : complete[..^1];
        await File.WriteAllBytesAsync(files.JournalPath, journal);

        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
    }

    [Test]
    [Arguments(0)]
    [Arguments(4)]
    public async Task AcWal004UnsupportedIdentityVersionFailsBeforeJournalMutation(int version)
    {
        using var files = new WalFileFixture();
        var identity = files.Initialize() with { FormatVersion = version };
        await files.WriteIdentityAsync(identity);
        await files.AssertRejectedUnchanged([], await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
    }
}
