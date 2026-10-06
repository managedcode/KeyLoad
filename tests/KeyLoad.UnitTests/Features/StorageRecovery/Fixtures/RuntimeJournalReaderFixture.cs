using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.StorageRecovery.Serialization;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class RuntimeJournalReaderFixture : IDisposable
{
    internal const ulong UnsupportedIdentityMagicBit = 1;
    internal const string GuidFormat = "N";
    internal const string RuntimeJournalNamespace = StoreReaderContract.RuntimeJournalKeySpace;
    internal const string PayloadName = "reader-contract-test";
    internal const string RootPrefix = "keyload-runtime-journal-reader-";
    private const string InvalidCurrentIdentity = "The current native identity envelope is invalid.";

    private readonly string root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString(GuidFormat));

    internal RuntimeJournalReaderFixture()
    {
        Directory.CreateDirectory(root);
    }

    internal Guid Incarnation { get; } = Guid.NewGuid();
    internal string CanonicalPath => Path.Combine(root, "canonical");
    internal string ReplicaPath => Path.Combine(root, "replica");
    internal string SnapshotPath => Path.Combine(root, "transfer.snapshot");
    internal string BackupPath => Path.Combine(root, "backup");
    internal string RestorePath => Path.Combine(root, "restored");
    internal static string IdentityPath(string directory) => Path.Combine(directory, "identity.json");

    internal static async Task RewriteCapabilityAsync(string directory, int capability)
    {
        var path = IdentityPath(directory);
        var bytes = await File.ReadAllBytesAsync(path);
        var envelope = NativeSerialization.Deserialize<ZoneTreeIdentityEnvelope>(bytes.AsSpan(sizeof(ulong)));
        var identity = NativeSerialization.Deserialize<StoreIdentity>(envelope.Payload) with
        { MinimumReaderContract = capability };
        var payload = NativeSerialization.Serialize(identity);
        var changed = ZoneTreeMetadataBinary.Write(new ZoneTreeIdentityEnvelope(payload, SHA256.HashData(payload)),
            ZoneTreeMetadataBinary.IdentityMagic);
        await File.WriteAllBytesAsync(path, changed);
    }

    internal static async Task OmitReaderCapabilityAsync(string directory)
    {
        var path = IdentityPath(directory);
        var maximumBytes = UnitExecutionOptions.StorageExecution().Value.MaximumIdentityFileBytes;
        var original = RequireCurrentEnvelope(await File.ReadAllBytesAsync(path), maximumBytes);
        var identity = NativeSerialization.Deserialize<StoreIdentity>(original.Payload);
        if (identity.MinimumReaderContract != StoreReaderContract.RuntimeJournal)
        {
            throw new InvalidDataException(InvalidCurrentIdentity);
        }
        var payload = NativeIdentityCapabilityOmission.SerializeWithoutCapability(identity, maximumBytes);
        var changed = ZoneTreeMetadataBinary.Write(new ZoneTreeIdentityEnvelope(payload, SHA256.HashData(payload)),
            ZoneTreeMetadataBinary.IdentityMagic);
        await File.WriteAllBytesAsync(path, changed);
        var rewritten = RequireCurrentEnvelope(await File.ReadAllBytesAsync(path), maximumBytes);
        NativeIdentityCapabilityOmission.VerifyCapabilityOmitted(rewritten.Payload);
    }

    private static ZoneTreeIdentityEnvelope RequireCurrentEnvelope(byte[] bytes, int maximumBytes)
    {
        if (bytes.Length > maximumBytes || bytes.Length <= sizeof(ulong)
            || BinaryPrimitives.ReadUInt64LittleEndian(bytes) != ZoneTreeMetadataBinary.IdentityMagic)
        {
            throw new InvalidDataException(InvalidCurrentIdentity);
        }
        var envelope = NativeSerialization.Deserialize<ZoneTreeIdentityEnvelope>(bytes.AsSpan(sizeof(ulong)));
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload), envelope.Checksum))
        {
            throw new InvalidDataException(InvalidCurrentIdentity);
        }
        return envelope;
    }

    internal static async Task CorruptIdentityMagicAsync(string directory)
    {
        var path = IdentityPath(directory);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[0] ^= (byte)UnsupportedIdentityMagicBit;
        await File.WriteAllBytesAsync(path, bytes);
    }

    internal static async Task<Dictionary<string, byte[]>> ReadFilesAsync(string directory)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            files.Add(Path.GetRelativePath(directory, path), await File.ReadAllBytesAsync(path));
        }
        return files;
    }

    internal static async Task<Dictionary<string, byte[]?>> ReadInventoryAsync(string directory)
    {
        var inventory = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            inventory.Add(Path.GetRelativePath(directory, path), null);
        }
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            inventory.Add(Path.GetRelativePath(directory, path), await File.ReadAllBytesAsync(path));
        }
        return inventory;
    }

    internal static byte[] RuntimeJournalKey => KeyCodec.Encode(RuntimeJournalNamespace, PayloadName);
    internal static byte[] FollowupKey => KeyCodec.Encode(RuntimeJournalNamespace, FollowupName);
    internal const string FollowupName = "reader-contract-followup";
    internal static byte[] RuntimeJournalValue => [0x52, 0x4A, 0x31, 0xA7];
    internal static byte[] FollowupValue => [0x52, 0x4A, 0x32, 0xB8];
    internal static byte[] CanonicalDocumentKey => [0x11];
    internal static byte[] CanonicalDocumentValue => [0xA1];
    internal static byte[] ReplicaDocumentKey => [0x22];
    internal static byte[] ReplicaDocumentValue => [0xB2];
    internal static byte[] PartialCanonicalValue => [0xC3];
    internal static byte[] PartialReplicaValue => [0xD4];
    internal static byte[] PartialCanonicalKey => [0x33];
    internal static byte[] PartialReplicaKey => [0x44];
    internal static byte[] TargetDocumentKey => [0x70];
    internal static byte[] TargetDocumentValue => [0x80];

    internal ZoneTreeStore Open(string directory, Guid? incarnation = null)
    {
        var options = new ZoneTreeStoreOptions(directory) { Incarnation = incarnation ?? Incarnation };
        return new ZoneTreeStore(options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
    }

    internal static void WriteRuntimeRecord(ZoneTreeStore store, byte[] key, byte[] value)
        => store.Commit((transaction, _) => { transaction.Put(key, value); return true; });

    internal static byte[]? Read(ZoneTreeStore store, byte[] key)
        => store.Read(view => view.ReadOwnedValue(key));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
