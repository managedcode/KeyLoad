using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class RuntimeJournalReaderFenceTests
{
    [Test]
    public async Task Ac002UnsupportedIdentityMagicRejectsWithoutChangingFiles()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        StoreIdentity originalIdentity;
        long originalPosition;
        using (var created = fixture.Open(fixture.CanonicalPath))
        {
            RuntimeJournalReaderFixture.WriteRuntimeRecord(created, RuntimeJournalReaderFixture.RuntimeJournalKey,
                RuntimeJournalReaderFixture.RuntimeJournalValue);
            originalIdentity = created.Identity;
            originalPosition = created.Position;
        }

        var originalBytes = await File.ReadAllBytesAsync(
            RuntimeJournalReaderFixture.IdentityPath(fixture.CanonicalPath));
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
        await RuntimeJournalReaderRepairContinuation.RequireAsync(fixture, originalIdentity, originalPosition,
            originalBytes, TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
    }
}
