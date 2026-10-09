using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class RuntimeJournalReaderRepairContinuation
{
    private const long FollowupCommitCount = 1;

    internal static async Task RequireAsync(RuntimeJournalReaderFixture fixture, StoreIdentity originalIdentity,
        long originalPosition, byte[] originalBytes, CancellationToken cancellationToken)
    {
        var path = RuntimeJournalReaderFixture.IdentityPath(fixture.CanonicalPath);
        await File.WriteAllBytesAsync(path, originalBytes, cancellationToken).ConfigureAwait(false);
        await Assert.That(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false))
            .IsEquivalentTo(originalBytes, CollectionOrdering.Matching);
        using (var repaired = fixture.Open(fixture.CanonicalPath))
        {
            await RequireAuthorityAsync(originalIdentity, repaired.Identity).ConfigureAwait(false);
            await Assert.That(repaired.Position).IsEqualTo(originalPosition);
            await RequireOriginalAsync(repaired).ConfigureAwait(false);
            await Assert.That(RuntimeJournalReaderFixture.Read(repaired, RuntimeJournalReaderFixture.FollowupKey)).IsNull();
            RuntimeJournalReaderFixture.WriteRuntimeRecord(repaired, RuntimeJournalReaderFixture.FollowupKey,
                RuntimeJournalReaderFixture.FollowupValue);
            await Assert.That(repaired.Position).IsEqualTo(originalPosition + FollowupCommitCount);
        }
        using var reopened = fixture.Open(fixture.CanonicalPath);
        await RequireAuthorityAsync(originalIdentity, reopened.Identity).ConfigureAwait(false);
        await Assert.That(reopened.Position).IsEqualTo(originalPosition + FollowupCommitCount);
        await RequireOriginalAsync(reopened).ConfigureAwait(false);
        await Assert.That(RuntimeJournalReaderFixture.Read(reopened, RuntimeJournalReaderFixture.FollowupKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.FollowupValue, CollectionOrdering.Matching);
    }

    private static async Task RequireOriginalAsync(ZoneTreeStore store)
        => await Assert.That(RuntimeJournalReaderFixture.Read(store, RuntimeJournalReaderFixture.RuntimeJournalKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);

    private static async Task RequireAuthorityAsync(StoreIdentity expected, StoreIdentity actual)
    {
        await WalFileFixture.AssertPreservedIdentity(expected, actual).ConfigureAwait(false);
        await Assert.That(actual.FormatVersion).IsEqualTo(expected.FormatVersion);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(expected.MinimumReaderContract);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
    }
}
