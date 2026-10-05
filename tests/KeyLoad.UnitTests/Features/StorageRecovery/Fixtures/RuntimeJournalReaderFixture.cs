using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class RuntimeJournalReaderFixture : IDisposable
{
    internal const ulong LegacyIdentityMagic = 0x354449444C4BUL;
    internal const int IdentityMagicBytes = sizeof(ulong);
    internal const int ChangedMagicBit = 1;
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
    internal string IdentityPath(string directory) => Path.Combine(directory, "identity.json");
    internal byte[] RuntimeJournalKey => KeyCodec.Encode(RuntimeJournalNamespace, PayloadName);
    internal byte[] FollowupKey => KeyCodec.Encode(RuntimeJournalNamespace, FollowupName);
    internal const string FollowupName = "reader-contract-followup";
    internal byte[] RuntimeJournalValue => [0x52, 0x4A, 0x31, 0xA7];
    internal byte[] FollowupValue => [0x52, 0x4A, 0x32, 0xB8];

    internal ZoneTreeStore Open(string directory)
    {
        var options = new ZoneTreeStoreOptions(directory) { Incarnation = Incarnation };
        return new ZoneTreeStore(options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
    }

    internal string CreateDirectory(string name)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        return directory;
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
