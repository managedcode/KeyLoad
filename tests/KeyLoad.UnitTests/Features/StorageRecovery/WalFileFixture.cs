using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class WalFileFixture : IDisposable
{
    internal const ulong CurrentMagic = 0x344C4157444C4BUL;
    internal const ulong LegacyMagic = 0x314C4157444C4BUL;
    internal const int HeaderBytes = 52;
    internal const int CurrentIdentityVersion = 6;
    private const int PayloadLengthOffset = 8;
    private const int SequenceOffset = 12;
    private const int ChecksumOffset = 20;
    private const int ChecksumBytes = 32;
    private const int FileBufferBytes = 4_096;
    private const string DirectoryPrefix = "keyload-orleans-wal-";
    private const string GuidFormat = "N";
    private const string JournalFileName = "commands.wal";
    private const string IdentityFileName = "identity.json";
    private const string TreeDirectoryName = "tree";
    private const string AllFilesPattern = "*";

    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(),
        DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    internal string JournalPath => Path.Combine(DirectoryPath, JournalFileName);
    internal string IdentityPath => Path.Combine(DirectoryPath, IdentityFileName);

    internal Task<byte[]> ReadJournalAsync() => ReadJournalAsync(JournalPath);

    internal static async Task<byte[]> ReadJournalAsync(string path)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            FileBufferBytes, FileOptions.Asynchronous);
        var bytes = new byte[file.Length];
        await file.ReadExactlyAsync(bytes);
        return bytes;
    }

    internal StoreIdentity Initialize()
    {
        using var store = new ZoneTreeStore(new(DirectoryPath));
        return store.Identity;
    }

    internal void RemoveMaterializedTree() => Directory.Delete(Path.Combine(DirectoryPath, TreeDirectoryName), true);

    internal Task WriteIdentityAsync(StoreIdentity identity)
    {
        ZoneTreeIdentityFile.Write(IdentityPath, identity);
        return Task.CompletedTask;
    }

    internal Task<StoreIdentity> ReadIdentityAsync()
        => Task.FromResult(ZoneTreeIdentityFile.Read(IdentityPath));

    // This independent JSON envelope is exclusively the historical format refusal fixture.
    internal async Task WriteLegacyJsonIdentityAsync(StoreIdentity identity)
    {
        var payload = JsonDefaults.Serialize(identity);
        await File.WriteAllBytesAsync(IdentityPath,
            JsonDefaults.Serialize(new IdentityEnvelope(payload, SHA256.HashData(payload))));
    }

    internal async Task<Dictionary<string, byte[]>> CaptureFilesAsync()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, AllFilesPattern, SearchOption.AllDirectories))
        {
            files.Add(Path.GetRelativePath(DirectoryPath, path), await File.ReadAllBytesAsync(path));
        }
        return files;
    }

    internal async Task AssertFilesUnchangedAsync(Dictionary<string, byte[]> expected)
    {
        var actual = await CaptureFilesAsync();
        await Assert.That(actual.Keys).IsEquivalentTo(expected.Keys);
        foreach (var entry in expected)
        {
            await Assert.That(actual[entry.Key]).IsEquivalentTo(entry.Value, CollectionOrdering.Matching);
        }
    }

    internal static byte[] CreateFrame(byte[] payload, long sequence = 1, ulong magic = CurrentMagic)
    {
        var frame = new byte[HeaderBytes + payload.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(frame, magic);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(PayloadLengthOffset), payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(SequenceOffset), sequence);
        SHA256.HashData(payload, frame.AsSpan(ChecksumOffset, ChecksumBytes));
        payload.CopyTo(frame, HeaderBytes);
        return frame;
    }

    internal async Task AssertRejectedUnchanged(byte[] journal, byte[] identity, ErrorCode expected)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var attempted = new ZoneTreeStore(new(DirectoryPath));
        });
        await Assert.That(failure.Code).IsEqualTo(expected);
        await Assert.That(await ReadJournalAsync()).IsEquivalentTo(journal, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(IdentityPath)).IsEquivalentTo(identity, CollectionOrdering.Matching);
    }

    internal static async Task AssertPreservedIdentity(StoreIdentity expected, StoreIdentity actual)
    {
        await Assert.That(actual.FormatVersion).IsEqualTo(CurrentIdentityVersion);
        await Assert.That(actual.KeyCodecVersion).IsEqualTo(expected.KeyCodecVersion);
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.SigningKey.Span.SequenceEqual(expected.SigningKey.Span)).IsTrue();
        await Assert.That(actual.Durability).IsEqualTo(expected.Durability);
        await Assert.That(actual.DispatchPaused).IsEqualTo(expected.DispatchPaused);
        await Assert.That(actual.ReadGeneration).IsEqualTo(expected.ReadGeneration);
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, true);
        }
    }

    private sealed record IdentityEnvelope(byte[] Payload, byte[] Checksum);
}
