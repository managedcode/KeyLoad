using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class CurrentStorageFixture : IDisposable
{
    internal const int CurrentFormatVersion = 7;
    private const string RootPrefix = "keyload-current-storage-";
    private const string SourceName = "source";
    private const string BackupName = "backup";
    private const string RestoredName = "restored";
    private const string SnapshotName = "snapshot.bin";
    private const string InvalidSnapshotName = "invalid-snapshot.bin";
    private readonly string root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));

    internal CurrentStorageFixture() => Directory.CreateDirectory(root);

    internal string Source => Path.Combine(root, SourceName);
    internal string Backup => Path.Combine(root, BackupName);
    internal string Restored => Path.Combine(root, RestoredName);
    internal string Snapshot => Path.Combine(root, SnapshotName);
    internal string InvalidSnapshot => Path.Combine(root, InvalidSnapshotName);
    internal ZoneTreeStoreOptions SourceOptions => new(Source);
    internal byte[] FirstKey { get; } = [0x00, 0xFF, 0x31];
    internal byte[] FirstValue { get; } = [0x80, 0x00, 0x7F];
    internal byte[] SecondKey { get; } = [0x01, 0x00, 0xFE];
    internal byte[] SecondValue { get; } = [0xFF, 0x02, 0x00, 0x81];

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
