using System.Buffers.Binary;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class RuntimeJournalReaderFenceTests
{
    private const int UnsupportedReaderContract = StoreReaderContract.RuntimeJournal + 1;
    private const int IdentityMagicOffset = 0;

    [Test]
    public async Task AcOrl013UnsupportedReaderRequirementLeavesHealthyStoreUsableAndUnchanged()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        using var store = fixture.Open(fixture.CanonicalPath);
        RuntimeJournalReaderFixture.WriteRuntimeRecord(store, RuntimeJournalReaderFixture.RuntimeJournalKey, RuntimeJournalReaderFixture.RuntimeJournalValue);
        var identity = store.Identity;
        var position = store.Position;
        var identityBytes = await File.ReadAllBytesAsync(RuntimeJournalReaderFixture.IdentityPath(fixture.CanonicalPath));

        var error = Assert.ThrowsExactly<KeyLoadException>(() => store.RequireReaderContract(UnsupportedReaderContract));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(store.Identity).IsEqualTo(identity);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(await File.ReadAllBytesAsync(RuntimeJournalReaderFixture.IdentityPath(fixture.CanonicalPath)))
            .IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        RuntimeJournalReaderFixture.WriteRuntimeRecord(store, RuntimeJournalReaderFixture.FollowupKey, RuntimeJournalReaderFixture.FollowupValue);
        await Assert.That(RuntimeJournalReaderFixture.Read(store, RuntimeJournalReaderFixture.FollowupKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.FollowupValue, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcOrl013UnmarkedStoreRejectsRuntimeJournalSnapshotWithoutChangingCanonicalRecords()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        using (var source = fixture.Open(fixture.CanonicalPath))
        {
            RuntimeJournalReaderFixture.WriteRuntimeRecord(source, RuntimeJournalReaderFixture.RuntimeJournalKey, RuntimeJournalReaderFixture.RuntimeJournalValue);
            source.CreateSnapshot(fixture.SnapshotPath, expectedAppliedPosition: 0);
        }

        using var target = fixture.Open(fixture.ReplicaPath);
        RuntimeJournalReaderFixture.WriteRuntimeRecord(target, RuntimeJournalReaderFixture.TargetDocumentKey, RuntimeJournalReaderFixture.TargetDocumentValue);
        var priorIdentity = target.Identity;
        var priorPosition = target.Position;
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => target.InstallSnapshot(fixture.SnapshotPath, 0));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(target.Identity).IsEqualTo(priorIdentity);
        await Assert.That(target.Position).IsEqualTo(priorPosition);
        await Assert.That(RuntimeJournalReaderFixture.Read(target, RuntimeJournalReaderFixture.TargetDocumentKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.TargetDocumentValue, CollectionOrdering.Matching);
        await Assert.That(RuntimeJournalReaderFixture.Read(target, RuntimeJournalReaderFixture.RuntimeJournalKey)).IsNull();

        target.RequireReaderContract(StoreReaderContract.RuntimeJournal);
        var installed = target.InstallSnapshot(fixture.SnapshotPath, 0);
        await Assert.That(installed.AppliedPosition).IsEqualTo(0L);
        await Assert.That(target.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
        await Assert.That(RuntimeJournalReaderFixture.Read(target, RuntimeJournalReaderFixture.RuntimeJournalKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue, CollectionOrdering.Matching);
        await Assert.That(RuntimeJournalReaderFixture.Read(target, RuntimeJournalReaderFixture.TargetDocumentKey)).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcOrl013ReaderContractMagicAndIdentityMustRemainCoherentOnDisk(bool upgraded)
    {
        using var fixture = new RuntimeJournalReaderFixture();
        using (var store = fixture.Open(fixture.CanonicalPath))
        {
            RuntimeJournalReaderFixture.WriteRuntimeRecord(store, RuntimeJournalReaderFixture.CanonicalDocumentKey,
                RuntimeJournalReaderFixture.CanonicalDocumentValue);
            if (upgraded)
            {
                store.RequireReaderContract(StoreReaderContract.RuntimeJournal);
            }
        }

        var path = RuntimeJournalReaderFixture.IdentityPath(fixture.CanonicalPath);
        var bytes = await File.ReadAllBytesAsync(path);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(IdentityMagicOffset), upgraded
            ? RuntimeJournalReaderFixture.LegacyIdentityMagic : RuntimeJournalReaderFixture.RuntimeJournalIdentityMagic);
        await File.WriteAllBytesAsync(path, bytes);
        var beforeOpen = await File.ReadAllBytesAsync(path);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var rejected = fixture.Open(fixture.CanonicalPath);
        });

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(beforeOpen, CollectionOrdering.Matching);
    }
}
