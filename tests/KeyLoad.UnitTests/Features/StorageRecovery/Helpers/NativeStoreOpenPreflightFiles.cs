using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeStoreOpenPreflightFiles : IDisposable
{
    internal const long TailPosition = 2;
    internal const long NextPosition = 3;
    private const int MaximumFrameBytes = 33_554_432;
    private const byte ChangedByte = 1;
    internal static readonly byte[] TailValue = "verified native tail"u8.ToArray();

    internal ZoneTreeExistingStoreFixture Source { get; } = new();

    internal void Compact()
    {
        using var store = new ZoneTreeStore(Source.Options);
        store.Compact();
    }

    internal byte[] Frame(long position = TailPosition)
        => WalFileFixture.CreateFrame(ZoneTreeJournalCodec.Serialize(
            [new StorageMutation(Source.Key, TailValue)], MaximumFrameBytes), position);

    internal async Task AppendAsync(byte[] bytes)
    {
        await using var output = new FileStream(Source.JournalPath, FileMode.Append, FileAccess.Write);
        await output.WriteAsync(bytes);
    }

    internal async Task<Dictionary<string, string?>> CaptureHashesAsync()
    {
        var entries = await Source.CaptureStoreEntriesAsync();
        return entries.ToDictionary(pair => pair.Key,
            pair => pair.Value is { } bytes ? Convert.ToHexStringLower(SHA256.HashData(bytes)) : null,
            StringComparer.Ordinal);
    }

    internal async Task AssertHashesUnchangedAsync(Dictionary<string, string?> expected)
    {
        var actual = await CaptureHashesAsync();
        await Assert.That(actual.Keys).IsEquivalentTo(expected.Keys);
        foreach (var pair in expected)
        {
            await Assert.That(actual[pair.Key]).IsEqualTo(pair.Value);
        }
    }

    internal async Task DamageCheckpointAsync()
    {
        var bytes = await File.ReadAllBytesAsync(Source.JournalPath);
        bytes[^1] ^= ChangedByte;
        await File.WriteAllBytesAsync(Source.JournalPath, bytes);
    }

    internal async Task SetCheckpointMagicAsync(ulong magic)
    {
        var bytes = await File.ReadAllBytesAsync(Source.JournalPath);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes, magic);
        await File.WriteAllBytesAsync(Source.JournalPath, bytes);
    }

    public void Dispose() => Source.Dispose();
}
