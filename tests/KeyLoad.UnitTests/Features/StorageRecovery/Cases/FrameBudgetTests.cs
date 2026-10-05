using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class FrameBudgetTests
{
    private const int BoundaryTrials = 12;
    private const int FirstValueBytesPerTrial = 17;
    private const int FinalValueBytesPerTrial = 19;
    private const int FinalValueMinimumBytes = 5;
    private const int FirstValueSalt = 13;
    private const int FinalValueSalt = 71;
    private const int BytePatternStride = 37;
    private const int ByteValueCount = 256;
    private const int FrameLengthOffset = 8;
    private const int FramePayloadOffset = 52;
    private const int SequenceOffset = 12;
    private const int ChecksumOffset = 20;
    private const int ChecksumLength = 32;
    private const int EmptyJournalBytes = 0;
    private const long JournalPosition = 1;
    private const ulong JournalMagic = 0x344C4157444C4BUL;
    private const string TemporaryDirectoryPrefix = "keyload-frame-accounting-";
    private const string JournalFileName = "commands.wal";
    private const string OneByteShortDirectorySuffix = "-one-byte-short";

    [Test]
    public async Task AcWal002IncrementalFrameAccountingMatchesBinaryPayloadAtExactLimits()
    {
        var root = Path.Combine(Path.GetTempPath(), TemporaryDirectoryPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            for (var trial = 0; trial < BoundaryTrials; trial++)
            {
                await VerifyBoundaryTrial(root, trial);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task VerifyBoundaryTrial(string root, int trial)
    {
        var first = CreateValueBytes(trial * FirstValueBytesPerTrial, trial, FirstValueSalt);
        var last = CreateValueBytes(trial * FinalValueBytesPerTrial + FinalValueMinimumBytes, trial, FinalValueSalt);
        byte[] valueKey = [0xFB, 0xFF];
        byte[] tombstoneKey = [0xFF, 0xFB];
        StorageMutation[] final = [new(valueKey, last), new(tombstoneKey, null)];
        var payload = ZoneTreeJournalCodec.Serialize(final, int.MaxValue);
        var directory = Path.Combine(root, trial.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var store = new ZoneTreeStore(new(directory) { MaxFrameBytes = payload.Length }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        CommitBoundaryMutations(store, valueKey, first, last, tombstoneKey);
        await VerifyOneByteShortLimit(root, trial, payload, valueKey, first, last, tombstoneKey);

        byte[] journal;
        using (var file = new FileStream(Path.Combine(directory, JournalFileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            journal = new byte[file.Length];
            await file.ReadExactlyAsync(journal);
        }

        await Assert.That(journal).IsEquivalentTo(CreateExpectedFrame(payload), CollectionOrdering.Matching);
        await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(journal.AsSpan(FrameLengthOffset))).IsEqualTo(payload.Length);
        await Assert.That(journal.AsSpan(FramePayloadOffset).ToArray()).IsEquivalentTo(payload, CollectionOrdering.Matching);
        await Assert.That(store.Read(view => view.ReadOwnedValue(valueKey))).IsEquivalentTo(last, CollectionOrdering.Matching);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => store.Commit((tx, _) =>
        {
            tx.Put(valueKey, new byte[payload.Length]);
            return true;
        })).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(store.Position).IsEqualTo(1);
        await Assert.That(store.Read(view => view.ReadOwnedValue(valueKey))).IsEquivalentTo(last, CollectionOrdering.Matching);
    }

    private static async Task VerifyOneByteShortLimit(string root, int trial, byte[] payload,
        byte[] valueKey, byte[] first, byte[] last, byte[] tombstoneKey)
    {
        var directory = Path.Combine(root,
            trial.ToString(System.Globalization.CultureInfo.InvariantCulture) + OneByteShortDirectorySuffix);
        using var store = new ZoneTreeStore(new(directory) { MaxFrameBytes = payload.Length - 1 }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            CommitBoundaryMutations(store, valueKey, first, last, tombstoneKey));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(store.Position).IsEqualTo(0);
        await Assert.That(new FileInfo(Path.Combine(directory, JournalFileName)).Length).IsEqualTo(EmptyJournalBytes);
        await Assert.That(store.Read(view => view.ReadOwnedValue(valueKey))).IsNull();
        await Assert.That(store.Read(view => view.ReadOwnedValue(tombstoneKey))).IsNull();
    }

    private static void CommitBoundaryMutations(ZoneTreeStore store, byte[] valueKey,
        byte[] first, byte[] last, byte[] tombstoneKey)
    {
        store.Commit((tx, _) =>
        {
            tx.Put(valueKey, first);
            tx.ValidateCommit();
            tx.Put(valueKey, last);
            tx.Delete(tombstoneKey);
            tx.ValidateCommit();
            return true;
        });
    }

    private static byte[] CreateValueBytes(int length, int trial, int salt)
    {
        var bytes = new byte[length];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)((index * BytePatternStride + trial + salt) % ByteValueCount);
        }

        return bytes;
    }

    private static byte[] CreateExpectedFrame(byte[] payload)
    {
        var frame = new byte[FramePayloadOffset + payload.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(frame, JournalMagic);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(FrameLengthOffset), payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(SequenceOffset), JournalPosition);
        SHA256.HashData(payload, frame.AsSpan(ChecksumOffset, ChecksumLength));
        payload.CopyTo(frame, FramePayloadOffset);
        return frame;
    }
}
