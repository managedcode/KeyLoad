using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class RuntimeJournalReaderFenceTests
{
    private const int UnsupportedReaderContract = StoreReaderContract.RuntimeJournal + 1;

    [Test]
    [Arguments(StoreReaderContract.Unspecified)]
    [Arguments(UnsupportedReaderContract)]
    public async Task AcNative002ZeroOrUnknownCapabilityRejectsWithoutFileMutation(int capability)
    {
        using var fixture = new RuntimeJournalReaderFixture();
        using (var created = fixture.Open(fixture.CanonicalPath))
        {
            await Assert.That(created.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
            RuntimeJournalReaderFixture.WriteRuntimeRecord(created, RuntimeJournalReaderFixture.RuntimeJournalKey,
                RuntimeJournalReaderFixture.RuntimeJournalValue);
        }

        await RuntimeJournalReaderFixture.RewriteCapabilityAsync(fixture.CanonicalPath, capability);
        var before = await RuntimeJournalReaderFixture.ReadFilesAsync(fixture.CanonicalPath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var rejected = fixture.Open(fixture.CanonicalPath);
        });

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        var after = await RuntimeJournalReaderFixture.ReadFilesAsync(fixture.CanonicalPath);
        await Assert.That(after.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(before.Keys.Order(StringComparer.Ordinal),
            CollectionOrdering.Matching);
        foreach (var file in before)
        {
            await Assert.That(after[file.Key]).IsEquivalentTo(file.Value, CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task AcNative002UnsupportedIdentityMagicRejectsWithoutChangingNativeFiles()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        using (var created = fixture.Open(fixture.CanonicalPath))
        {
            RuntimeJournalReaderFixture.WriteRuntimeRecord(created, RuntimeJournalReaderFixture.RuntimeJournalKey,
                RuntimeJournalReaderFixture.RuntimeJournalValue);
        }

        await RuntimeJournalReaderFixture.CorruptIdentityMagicAsync(fixture.CanonicalPath);
        var before = await RuntimeJournalReaderFixture.ReadFilesAsync(fixture.CanonicalPath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var rejected = fixture.Open(fixture.CanonicalPath);
        });

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        var after = await RuntimeJournalReaderFixture.ReadFilesAsync(fixture.CanonicalPath);
        await Assert.That(after.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(before.Keys.Order(StringComparer.Ordinal),
            CollectionOrdering.Matching);
        foreach (var file in before)
        {
            await Assert.That(after[file.Key]).IsEquivalentTo(file.Value, CollectionOrdering.Matching);
        }
    }
}
