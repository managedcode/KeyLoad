using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PreparedTransactionTests
{
    private const int HeaderLength = 52;
    private const int PayloadLengthOffset = 8;
    private const int SequenceOffset = 12;
    private const int ChecksumOffset = 20;
    private const int JournalPayloadOffset = 52;
    private const ulong JournalMagic = 0x344C4157444C4BUL;
    private const string JournalFileName = "commands.wal";
    private const string TemporaryDirectoryPrefix = "keyload-prepared-transaction-";

    [Test]
    public async Task AcPsw002RepeatedValidationReplacementDeletionResetAndCallerBuffersKeepFinalFrame()
    {
        var root = CreateTemporaryDirectory();
        byte[] discardedKey = [0x10];
        byte[] discardedValue = [0x20];
        byte[] replacedKey = [0x30];
        byte[] finalValue = [0x40, 0x41, 0x42];
        byte[] finalDeleteKey = [0x50];
        StorageMutation[] expectedMutations = [new(replacedKey, finalValue), new(finalDeleteKey, null)];
        var expectedFrame = CreateExpectedFrame(expectedMutations, 1);

        try
        {
            using (var store = new ZoneTreeStore(new(root)))
            {
                store.Commit((transaction, _) =>
                {
                    transaction.Put(discardedKey, discardedValue);
                    transaction.ValidateCommit();
                    transaction.Put(replacedKey, discardedValue);
                    transaction.ValidateCommit();
                    transaction.Put(replacedKey, finalValue);
                    transaction.Delete(discardedKey);
                    transaction.ValidateCommit();
                    transaction.Reset();
                    transaction.Put(replacedKey, finalValue);
                    transaction.Delete(finalDeleteKey);
                    transaction.ValidateCommit();
                    transaction.ValidateCommit();
                    replacedKey[0] = 0x60;
                    finalValue[0] = 0x61;
                    finalDeleteKey[0] = 0x62;
                    return true;
                });

                await Assert.That(store.Position).IsEqualTo(1L);
                await AssertJournalEquals(Path.Combine(root, JournalFileName), expectedFrame);
                await Assert.That(store.Read(view => view.ReadOwnedValue([0x30]))).IsEquivalentTo(new byte[] { 0x40, 0x41, 0x42 }, CollectionOrdering.Matching);
                await Assert.That(store.Read(view => view.ReadOwnedValue([0x50]))).IsNull();
            }

            using var reopened = new ZoneTreeStore(new(root));
            await Assert.That(reopened.Position).IsEqualTo(1L);
            await Assert.That(reopened.Read(view => view.ReadOwnedValue([0x30]))).IsEquivalentTo(new byte[] { 0x40, 0x41, 0x42 }, CollectionOrdering.Matching);
            await Assert.That(reopened.Read(view => view.ReadOwnedValue([0x50]))).IsNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task AcPsw003RejectedOversizedStageRetainsValidatedSetForCommit()
    {
        var root = CreateTemporaryDirectory();
        byte[] key = [0x21];
        byte[] value = [0x31, 0x32];
        var expectedFrame = CreateExpectedFrame([new(key, value)], 1);
        KeyLoadException? rejection = null;

        try
        {
            using var store = new ZoneTreeStore(new(root) { MaxFrameBytes = expectedFrame.Length - HeaderLength });
            store.Commit((transaction, _) =>
            {
                transaction.Put(key, value);
                transaction.ValidateCommit();
                try
                {
                    transaction.Put(key, new byte[expectedFrame.Length]);
                }
                catch (KeyLoadException exception)
                {
                    rejection = exception;
                }

                transaction.ValidateCommit();
                return true;
            });

            await Assert.That(rejection).IsNotNull();
            await Assert.That(rejection!.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(store.Position).IsEqualTo(1L);
            await AssertJournalEquals(Path.Combine(root, JournalFileName), expectedFrame);
            await Assert.That(store.Read(view => view.ReadOwnedValue(key))).IsEquivalentTo(value, CollectionOrdering.Matching);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task AcPsw003ResetToEmptyWritesNoFrameAndDoesNotAdvancePosition()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var store = new ZoneTreeStore(new(root));
            store.Commit((transaction, _) =>
            {
                transaction.Put([0x71], [0x72]);
                transaction.ValidateCommit();
                transaction.Reset();
                transaction.ValidateCommit();
                return true;
            });

            await Assert.That(store.Position).IsEqualTo(0L);
            await Assert.That(new FileInfo(Path.Combine(root, JournalFileName)).Length).IsEqualTo(0L);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), TemporaryDirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static byte[] CreateExpectedFrame(StorageMutation[] mutations, long position)
    {
        var payload = ZoneTreeJournalCodec.Serialize(mutations, int.MaxValue);
        var frame = new byte[HeaderLength + payload.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(frame, JournalMagic);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(PayloadLengthOffset), payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(SequenceOffset), position);
        SHA256.HashData(payload, frame.AsSpan(ChecksumOffset, 32));
        payload.CopyTo(frame, JournalPayloadOffset);
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
