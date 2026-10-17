using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OrleansWalSuccessorTests
{
    private const long InitialAppliedPosition = 0;
    private const long ExpectedRecordCount = 1;
    private const string CheckpointFileName = "successor-checkpoint";
    private static readonly byte[] BaselineKey = [0x10];
    private static readonly byte[] BaselineValue = [0x30];
    private static readonly byte[] SuccessorKey = [0x20];
    private static readonly byte[] SuccessorValue = [0x40];

    [Test]
    public async Task AcWal003LastLegalSuccessorRecoversWholeFrame()
    {
        using var files = new WalFileFixture();
        var checkpoint = await CreateCheckpointAsync(files, long.MaxValue - 1);
        var identity = await File.ReadAllBytesAsync(files.IdentityPath);
        byte[] journal = [.. checkpoint, .. CreateSuccessorFrame(long.MaxValue)];
        await File.WriteAllBytesAsync(files.JournalPath, journal);

        using (var recovered = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            await Assert.That(recovered.Position).IsEqualTo(long.MaxValue);
            await Assert.That(recovered.Read(view => view.ReadOwnedValue(BaselineKey)))
                .IsEquivalentTo(BaselineValue, CollectionOrdering.Matching);
            await Assert.That(recovered.Read(view => view.ReadOwnedValue(SuccessorKey)))
                .IsEquivalentTo(SuccessorValue, CollectionOrdering.Matching);
            await AssertAuthorityBytesAsync(files, journal, identity);
        }

        AssertReleasedOwnership(files);
    }

    [Test]
    public async Task AcWal003WrappedSuccessorRejectsWithoutChangingAuthorityOrApplyingFrame()
    {
        using var files = new WalFileFixture();
        var checkpoint = await CreateCheckpointAsync(files, long.MaxValue);
        var identity = await File.ReadAllBytesAsync(files.IdentityPath);
        byte[] journal = [.. checkpoint, .. CreateSuccessorFrame(long.MinValue)];
        await File.WriteAllBytesAsync(files.JournalPath, journal);

        await files.AssertRejectedUnchanged(journal, identity, ErrorCode.Corruption);
        AssertReleasedOwnership(files);

        // Keep the derived tree from the failed attempt: deleting it would hide
        // a premature apply of SuccessorKey before the corruption was reported.
        await File.WriteAllBytesAsync(files.JournalPath, checkpoint);
        using (var recovered = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            await Assert.That(recovered.Position).IsEqualTo(long.MaxValue);
            await Assert.That(recovered.Read(view => view.ReadOwnedValue(BaselineKey)))
                .IsEquivalentTo(BaselineValue, CollectionOrdering.Matching);
            await Assert.That(recovered.Read(view => view.ReadOwnedValue(SuccessorKey))).IsNull();
            await AssertAuthorityBytesAsync(files, checkpoint, identity);
        }

        AssertReleasedOwnership(files);
    }

    private static async Task<byte[]> CreateCheckpointAsync(WalFileFixture files, long position)
    {
        StoreIdentity identity;
        using (var seeded = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            seeded.Commit((transaction, _) =>
            {
                transaction.Put(BaselineKey, BaselineValue);
                return true;
            });
            identity = seeded.Identity;
        }

        var options = new ZoneTreeStoreOptions(files.DirectoryPath)
            .ResolveExecutionOptions(UnitExecutionOptions.StorageExecution());
        var path = Path.Combine(files.DirectoryPath, CheckpointFileName);
        using (var tree = ZoneTreeTreeFactory.Open(options))
        {
            ZoneTreeCheckpointWriter.Write(path, options, identity, position, InitialAppliedPosition, tree);
        }

        using (var input = File.OpenRead(path))
        {
            var verified = ZoneTreeCheckpointReader.Read(input, options);
            await Assert.That(verified.Position).IsEqualTo(position);
            await Assert.That(verified.AppliedPosition).IsEqualTo(InitialAppliedPosition);
            await Assert.That(verified.RecordCount).IsEqualTo(ExpectedRecordCount);
            await Assert.That(input.Position).IsEqualTo(input.Length);
        }

        var checkpoint = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(files.JournalPath, checkpoint);
        files.RemoveMaterializedTree();
        return checkpoint;
    }

    private static byte[] CreateSuccessorFrame(long sequence)
    {
        using var serializer = new WalSerializerFixture();
        var payload = serializer.Serialize([new()
        {
            Key = SuccessorKey,
            Value = SuccessorValue,
            Kind = ZoneTreeJournalMutation.PutKind
        }]);
        return WalFileFixture.CreateFrame(payload, sequence);
    }

    private static async Task AssertAuthorityBytesAsync(WalFileFixture files, byte[] journal, byte[] identity)
    {
        await Assert.That(await files.ReadJournalAsync())
            .IsEquivalentTo(journal, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath))
            .IsEquivalentTo(identity, CollectionOrdering.Matching);
    }

    private static void AssertReleasedOwnership(WalFileFixture files)
    {
        using var ownership = new FileStream(
            Path.Combine(files.DirectoryPath, ZoneTreePersistenceFormat.OwnerLockFileName),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var journal = new FileStream(files.JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}
