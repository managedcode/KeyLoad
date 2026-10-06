using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class RuntimeJournalReaderFixture : IDisposable
{
    internal const ulong LegacyIdentityMagic = 0x354449444C4BUL;
    internal const ulong RuntimeJournalIdentityMagic = 0x364449444C4BUL;
    internal const string GuidFormat = "N";
    internal const string RuntimeJournalNamespace = StoreReaderContract.RuntimeJournalKeySpace;
    internal const string PayloadName = "reader-contract-test";
    internal const string RootPrefix = "keyload-runtime-journal-reader-";

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
