using System.Buffers.Binary;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

/// <summary>Owns one test store directory and its identity and tree helpers.</summary>
internal sealed class StoreFileFixture : IDisposable
{
    private const string DispatchPausedName = "dispatch-paused";
    private const string DirectoryPrefix = "keyload-store-files-";
    private const string GuidFormat = "N";
    private const string IdentityFileName = "identity.json";
    private const string AllFilesPattern = "*";

    internal string DirectoryPath
    {
        get;
    } = Path.Combine(AppContext.BaseDirectory,
        DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    internal string IdentityPath => Path.Combine(DirectoryPath, IdentityFileName);

    /// <summary>Gets the store position record committed with every transaction.</summary>
    internal static byte[] PositionKey => KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
        ZoneTreePersistenceFormat.PositionKey);

    /// <summary>Gets the dispatch-paused record that a restore commits into the restored store.</summary>
    internal static byte[] DispatchPausedKey => KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
        DispatchPausedName);

    /// <summary>Encodes the store position record value for an exact committed position.</summary>
    internal static byte[] PositionValue(long position)
    {
        var value = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(value, position);
        return value;
    }

    internal StoreIdentity Initialize()
    {
        using var store = new ZoneTreeStore(new(DirectoryPath), UnitExecutionOptions.StorageExecution());
        return store.Identity;
    }

    internal Task WriteIdentityAsync(StoreIdentity identity)
    {
        ZoneTreeIdentityFile.Write(IdentityPath, identity, UnitExecutionOptions.StorageExecution().Value.IdentityBufferBytes);
        return Task.CompletedTask;
    }

    internal Task<StoreIdentity> ReadIdentityAsync()
    {
        var settings = UnitExecutionOptions.StorageExecution().Value;
        return Task.FromResult(ZoneTreeIdentityFile.Read(IdentityPath, settings.MaximumIdentityFileBytes, settings.StreamBufferBytes));
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

    internal async Task AssertRejectedAsync(byte[] identity, ErrorCode expected)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var attempted = new ZoneTreeStore(new(DirectoryPath), UnitExecutionOptions.StorageExecution());
        });
        await Assert.That(failure.Code).IsEqualTo(expected);
        await Assert.That(await File.ReadAllBytesAsync(IdentityPath)).IsEquivalentTo(identity, CollectionOrdering.Matching);
    }

    internal static async Task AssertPreservedIdentity(StoreIdentity expected, StoreIdentity actual)
    {
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
}
