using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class TombstoneValueTests
{
    private const int FramePayloadOffset = 52;
    private const int FrameLengthOffset = 8;
    private const int SequenceOffset = 12;
    private const int ChecksumOffset = 20;
    private const int ChecksumLength = 32;
    private const long EmptyJournalLength = 0;
    private const ulong JournalMagic = 0x324C4157444C4BUL;
    private const string JournalFileName = "commands.wal";

    [Test]
    public async Task AcPsw002EmptyPutAndDeleteRemainDistinctThroughStageCommitCompactAndReopen()
    {
        var directory = CreateDirectory();
        var prefix = new byte[] { 0xA0 };
        var emptyKey = new byte[] { 0xA0, 0x01 };
        var deletedKey = new byte[] { 0xA0, 0x02 };
        var retainedKey = new byte[] { 0xA0, 0x03 };
        StorageSnapshot snapshot;
        try
        {
            using (var store = new ZoneTreeStore(new(directory)))
            {
                store.Commit((tx, _) =>
                {
                    tx.Put(emptyKey, [0xFF]);
                    tx.Put(deletedKey, [0xFE]);
                    tx.Put(retainedKey, [0xFD]);
                    return true;
                });
                await AssertStagedView(store, prefix, emptyKey, deletedKey, retainedKey);
                await AssertCommittedView(store, prefix, emptyKey, deletedKey, retainedKey);
            }

            using (var replayed = new ZoneTreeStore(new(directory)))
            {
                await AssertCommittedView(replayed, prefix, emptyKey, deletedKey, retainedKey);
                snapshot = replayed.Compact();
                await Assert.That(snapshot.RecordCount).IsEqualTo(2L);
                await AssertCommittedView(replayed, prefix, emptyKey, deletedKey, retainedKey);
            }

            using var reopened = new ZoneTreeStore(new(directory));
            await AssertCommittedView(reopened, prefix, emptyKey, deletedKey, retainedKey);
            await Assert.That(reopened.Position).IsEqualTo(snapshot.Position);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task AcPsw002EmptyPutAndDeleteUseCanonicalPayloadAtExactFrameLimit()
    {
        var root = CreateDirectory();
        var emptyKey = new byte[] { 0x10 };
        var deletedKey = new byte[] { 0x20 };
        var payload = ZoneTreeJournalCodec.Serialize(new StorageMutation[]
        {
            new(emptyKey, Array.Empty<byte>()),
            new(deletedKey, (ReadOnlyMemory<byte>?)null)
        }, int.MaxValue);
        try
        {
            var directory = Path.Combine(root, "exact");
            using (var store = new ZoneTreeStore(new(directory) { MaxFrameBytes = payload.Length }))
            {
                store.Commit((tx, _) =>
                {
                    tx.Put(emptyKey, []);
                    tx.Delete(deletedKey);
                    return true;
                });

                var expectedFrame = CreateFrame(payload, 1);
                await AssertJournalEquals(Path.Combine(directory, JournalFileName), expectedFrame);
                await AssertBinaryEmptyAndDeletePayload(payload, emptyKey, deletedKey);
            }

            using var tooSmall = new ZoneTreeStore(new(Path.Combine(root, "short"))
            { MaxFrameBytes = payload.Length - 1 });
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => tooSmall.Commit((tx, _) =>
            {
                tx.Put(emptyKey, []);
                tx.Delete(deletedKey);
                return true;
            }));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(tooSmall.Position).IsEqualTo(0L);
            await Assert.That(new FileInfo(Path.Combine(root, "short", JournalFileName)).Length)
                .IsEqualTo(EmptyJournalLength);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertBinaryEmptyAndDeletePayload(byte[] payload, byte[] emptyKey, byte[] deletedKey)
    {
        using var serializer = new WalSerializerFixture();
        var decoded = serializer.Deserialize(payload);
        await Assert.That(decoded.Length).IsEqualTo(2);
        await Assert.That(decoded[0].Key.Span.SequenceEqual(emptyKey)).IsTrue();
        await Assert.That(decoded[0].Kind).IsEqualTo(ZoneTreeJournalMutation.PutKind);
        await Assert.That(decoded[0].Value.HasValue).IsTrue();
        await Assert.That(decoded[0].Value!.Value.Length).IsEqualTo(0);
        await Assert.That(decoded[1].Key.Span.SequenceEqual(deletedKey)).IsTrue();
        await Assert.That(decoded[1].Kind).IsEqualTo(ZoneTreeJournalMutation.DeleteKind);
        await Assert.That(decoded[1].Value.HasValue).IsFalse();
    }

    private static async Task AssertCommittedView(ZoneTreeStore store, byte[] prefix,
        byte[] emptyKey, byte[] deletedKey, byte[] retainedKey)
    {
        var ownedEmpty = store.Read(view => view.ReadOwnedValue(emptyKey));
        var deletedOwned = store.Read(view => view.ReadOwnedValue(deletedKey));
        var borrowedLength = -1;
        var borrowedEmpty = store.Read(view => view.ReadValue(emptyKey, value => borrowedLength = value.Length));
        var deletedCallback = false;
        var borrowedDelete = store.Read(view => view.ReadValue(deletedKey, _ => deletedCallback = true));
        var range = store.Read(view => view.Scan(prefix, 10));
        var orderedKeys = range.Records.Select(record => Convert.ToHexString(record.Key.Span)).ToArray();

        await Assert.That(ownedEmpty).IsNotNull();
        await Assert.That(ownedEmpty).IsEmpty();
        await Assert.That(deletedOwned).IsNull();
        await Assert.That(borrowedEmpty).IsTrue();
        await Assert.That(borrowedLength).IsEqualTo(0);
        await Assert.That(borrowedDelete).IsFalse();
        await Assert.That(deletedCallback).IsFalse();
        await Assert.That(range.Records.Length).IsEqualTo(2);
        await Assert.That(orderedKeys).IsEquivalentTo(["A001", "A003"], CollectionOrdering.Matching);
        await Assert.That(range.Records[0].Key.Span.SequenceEqual(emptyKey)).IsTrue();
        await Assert.That(range.Records[0].Value.Length).IsEqualTo(0);
        await Assert.That(range.Records[1].Key.Span.SequenceEqual(retainedKey)).IsTrue();
        await Assert.That(range.Records[1].Value.Span.SequenceEqual(new byte[] { 0xFD })).IsTrue();
    }

    private static async Task AssertStagedView(ZoneTreeStore store, byte[] prefix,
        byte[] emptyKey, byte[] deletedKey, byte[] retainedKey)
    {
        var stagedOwnedLength = -1;
        var stagedEmptyLength = -1;
        var stagedEmptyRead = false;
        var stagedDeleteOwned = false;
        var stagedDeleteCallback = false;
        var stagedDeleteRead = true;
        var stagedRange = new List<string>();
        store.Commit((tx, _) =>
        {
            tx.Put(emptyKey, []);
            tx.Delete(deletedKey);
            stagedOwnedLength = tx.ReadOwnedValue(emptyKey)?.Length ?? -1;
            stagedDeleteOwned = tx.ReadOwnedValue(deletedKey) is null;
            stagedEmptyRead = tx.ReadValue(emptyKey, value => stagedEmptyLength = value.Length);
            stagedDeleteRead = tx.ReadValue(deletedKey, _ => stagedDeleteCallback = true);
            tx.VisitRange(prefix, 10, (key, value) =>
            {
                stagedRange.Add($"{Convert.ToHexString(key)}:{value.Length}");
                return true;
            });
            return true;
        });

        await Assert.That(stagedOwnedLength).IsEqualTo(0);
        await Assert.That(stagedEmptyRead).IsTrue();
        await Assert.That(stagedEmptyLength).IsEqualTo(0);
        await Assert.That(stagedDeleteOwned).IsTrue();
        await Assert.That(stagedDeleteRead).IsFalse();
        await Assert.That(stagedDeleteCallback).IsFalse();
        await Assert.That(stagedRange).IsEquivalentTo(
            ["A001:0", $"{Convert.ToHexString(retainedKey)}:1"], CollectionOrdering.Matching);
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "keyload-tombstone-values-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static byte[] CreateFrame(byte[] payload, long position)
    {
        var frame = new byte[FramePayloadOffset + payload.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(frame, JournalMagic);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(FrameLengthOffset), payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(SequenceOffset), position);
        SHA256.HashData(payload, frame.AsSpan(ChecksumOffset, ChecksumLength));
        payload.CopyTo(frame, FramePayloadOffset);
        return frame;
    }

    private static async Task AssertJournalEquals(string path, byte[] expected)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var actual = new byte[file.Length];
        await file.ReadExactlyAsync(actual);
        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
}
