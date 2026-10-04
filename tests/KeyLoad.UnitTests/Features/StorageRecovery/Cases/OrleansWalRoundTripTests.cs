using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OrleansWalRoundTripTests
{
    private const int RepresentativeValueBytes = 1_024;
    private const int PayloadLengthOffset = 8;
    private const int SequenceOffset = 12;
    private const int ChecksumOffset = 20;
    private const int ChecksumBytes = 32;
    private const long CommittedPosition = 2;

    [Test]
    public async Task AcWal001OfficialSerializerReadsRawBinaryMutationsAndReopenPreservesBytes()
    {
        using var files = new WalFileFixture();
        byte[] binaryKey = [0x00, 0xFF, 0x80];
        byte[] emptyKey = [0x10, 0x00];
        byte[] deletedKey = [0x20, 0xFF];
        byte[] binaryValue = [0x00, 0xFF, 0x80, 0xFE];
        long secondFrameOffset;
        using (var store = new ZoneTreeStore(new(files.DirectoryPath)))
        {
            store.Commit((transaction, _) => { transaction.Put(deletedKey, binaryValue); return true; });
            secondFrameOffset = new FileInfo(files.JournalPath).Length;
            store.Commit((transaction, _) =>
            {
                transaction.Put(binaryKey, binaryValue);
                transaction.Put(emptyKey, []);
                transaction.Delete(deletedKey);
                return true;
            });
            await Assert.That(store.Identity.FormatVersion).IsEqualTo(WalFileFixture.CurrentIdentityVersion);
        }

        await AssertRawFrame(files.JournalPath, secondFrameOffset, binaryKey, binaryValue, emptyKey, deletedKey);
        files.RemoveMaterializedTree();
        using var reopened = new ZoneTreeStore(new(files.DirectoryPath));
        await Assert.That(reopened.Position).IsEqualTo(CommittedPosition);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue(binaryKey)))
            .IsEquivalentTo(binaryValue, CollectionOrdering.Matching);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue(emptyKey))).IsNotNull();
        await Assert.That(reopened.Read(view => view.ReadOwnedValue(emptyKey))).IsEmpty();
        await Assert.That(reopened.Read(view => view.ReadOwnedValue(deletedKey))).IsNull();
    }

    [Test]
    public async Task AcWal001RealBinaryPayloadUsesFewerEncodedBytesThanEquivalentRepresentativeJson()
    {
        using var files = new WalFileFixture();
        byte[] key = [0x00, 0xFF, 0x80];
        var value = RandomNumberGenerator.GetBytes(RepresentativeValueBytes);
        using (var store = new ZoneTreeStore(new(files.DirectoryPath)))
        {
            store.Commit((transaction, _) => { transaction.Put(key, value); return true; });
        }
        var journal = await files.ReadJournalAsync();
        var payload = journal.AsSpan(WalFileFixture.HeaderBytes).ToArray();
        var json = JsonDefaults.Serialize(new StorageMutation[] { new(key, value) });
        using var serializer = new WalSerializerFixture();
        var mutations = serializer.Deserialize(payload);

        await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(journal.AsSpan(PayloadLengthOffset)))
            .IsEqualTo(payload.Length);
        await Assert.That(mutations.Length).IsEqualTo(1);
        await Assert.That(mutations[0].Kind).IsEqualTo(ZoneTreeJournalMutation.PutKind);
        await Assert.That(mutations[0].Key.Span.SequenceEqual(key)).IsTrue();
        await Assert.That(mutations[0].Value.HasValue).IsTrue();
        await Assert.That(mutations[0].Value!.Value.Span.SequenceEqual(value)).IsTrue();
        await Assert.That(payload.Length < json.Length).IsTrue();
    }

    private static async Task AssertRawFrame(string path, long offset,
        byte[] key, byte[] value, byte[] emptyKey, byte[] deletedKey)
    {
        var journal = await WalFileFixture.ReadJournalAsync(path);
        var frame = journal.AsSpan(checked((int)offset)).ToArray();
        var payload = frame.AsSpan(WalFileFixture.HeaderBytes).ToArray();
        await Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(frame)).IsEqualTo(WalFileFixture.CurrentMagic);
        await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(PayloadLengthOffset))).IsEqualTo(payload.Length);
        await Assert.That(BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(SequenceOffset))).IsEqualTo(CommittedPosition);
        await Assert.That(SHA256.HashData(payload)).IsEquivalentTo(
            frame.AsSpan(ChecksumOffset, ChecksumBytes).ToArray(), CollectionOrdering.Matching);
        using var serializer = new WalSerializerFixture();
        var mutations = serializer.Deserialize(payload);
        await Assert.That(mutations.Length).IsEqualTo(3);
        await Assert.That(mutations[0].Key.Span.SequenceEqual(key)).IsTrue();
        await Assert.That(mutations[0].Kind).IsEqualTo(ZoneTreeJournalMutation.PutKind);
        await Assert.That(mutations[0].Value.HasValue).IsTrue();
        await Assert.That(mutations[0].Value!.Value.Span.SequenceEqual(value)).IsTrue();
        await Assert.That(mutations[1].Key.Span.SequenceEqual(emptyKey)).IsTrue();
        await Assert.That(mutations[1].Kind).IsEqualTo(ZoneTreeJournalMutation.PutKind);
        await Assert.That(mutations[1].Value.HasValue).IsTrue();
        await Assert.That(mutations[1].Value!.Value.IsEmpty).IsTrue();
        await Assert.That(mutations[2].Key.Span.SequenceEqual(deletedKey)).IsTrue();
        await Assert.That(mutations[2].Kind).IsEqualTo(ZoneTreeJournalMutation.DeleteKind);
        await Assert.That(mutations[2].Value.HasValue).IsFalse();
    }
}
