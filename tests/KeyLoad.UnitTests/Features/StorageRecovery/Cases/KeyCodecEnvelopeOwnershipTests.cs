using System.Buffers;
using System.Runtime.InteropServices;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class KeyCodecEnvelopeOwnershipTests
{
    private const string DirectoryPrefix = "keycodec-owned-";

    [Test]
    public async Task NativeStageEnvelopeAndReopenPreserveOwnedKeyAndValueBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(directory))
        {
            throw new InvalidOperationException("The generated storage directory must be unowned.");
        }

        var key = KeyCodec.Encode("ownership", "sample\0key");
        var expectedKey = key.ToArray();
        var value = ArrayPool<byte>.Shared.Rent(128);
        var returned = false;

        try
        {
            Array.Fill(value, (byte)0x36);
            var expectedValue = value.ToArray();
            var envelope = NativeSerialization.Serialize(new[] { new StorageMutation(expectedKey, expectedValue) });
            using (var store = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
            {
                store.Commit((transaction, _) => StageAndMutateInputs(transaction, key, value));
                returned = true;
                ArrayPool<byte>.Shared.Return(value, clearArray: true);

                await VerifyCallerOwnedResultCannotChangeStoreAsync(store, expectedKey, expectedValue);
            }

            var mutations = NativeSerialization.Deserialize<StorageMutation[]>(envelope);
            await Assert.That(mutations.Length).IsEqualTo(1);
            await Assert.That(mutations[0].Key.Span.SequenceEqual(expectedKey)).IsTrue();
            await Assert.That(mutations[0].Value!.Value.Span.SequenceEqual(expectedValue)).IsTrue();

            using var reopened = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            await Assert.That(reopened.Position).IsEqualTo(1L);
            await Assert.That(reopened.Read(view => view.ReadOwnedValue(expectedKey))!.SequenceEqual(expectedValue)).IsTrue();
        }
        finally
        {
            if (!returned)
            {
                ArrayPool<byte>.Shared.Return(value, clearArray: true);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static bool StageAndMutateInputs(IAtomicTransaction transaction, byte[] key, byte[] value)
    {
        transaction.Put(key, value);
        Array.Fill(key, (byte)0xA5);
        Array.Fill(value, (byte)0x5A);
        return true;
    }

    private static async Task VerifyCallerOwnedResultCannotChangeStoreAsync(ZoneTreeStore store,
        byte[] expectedKey, byte[] expectedValue)
    {
        var page = store.Read(view => view.Scan(expectedKey, 1));
        await Assert.That(page.Records.Length).IsEqualTo(1);
        var record = page.Records[0];
        await Assert.That(MemoryMarshal.TryGetArray(record.Key, out var keySegment)).IsTrue();
        await Assert.That(MemoryMarshal.TryGetArray(record.Value, out var valueSegment)).IsTrue();
        keySegment.Array![keySegment.Offset] ^= 0xFF;
        valueSegment.Array![valueSegment.Offset] ^= 0xFF;

        var retained = store.Read(view => view.ReadOwnedValue(expectedKey));
        await Assert.That(retained!.SequenceEqual(expectedValue)).IsTrue();
    }
}
