using KeyLoad.ProcessRecoveryTests.StorageRecovery;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.BackupRestore;

/// <summary>Owns a closed source store with two commits and one backup of that cut.</summary>
internal sealed class BackupCutFixture : IDisposable
{
    internal const string StoredKey = "metadata/key";
    internal const string StoredValue = "metadata-value";
    internal const long InitialAuthorityValue = 1L;
    internal static readonly DateTimeOffset InitialClock = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string RootPrefix = "keyload-execution-backup-cut-";
    private const string SourceDirectoryName = "source";
    private const string BackupDirectoryName = "backup";
    private const string RestoredDirectoryName = "restored";
    private readonly string root = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(AppContext.BaseDirectory)),
        RootPrefix + Guid.NewGuid().ToString("N"));

    internal string Source => Path.Combine(root, SourceDirectoryName);
    internal string Backup => Path.Combine(root, BackupDirectoryName);
    internal string Destination => Path.Combine(root, RestoredDirectoryName);
    internal static byte[] StoredKeyBytes => KeyCodec.Encode(StoredKey);
    internal StoreIdentity Identity
    {
        get; private set;
    } = null!;
    internal long Position
    {
        get; private set;
    }

    private BackupCutFixture()
    {
    }

    internal static async Task<BackupCutFixture> CreateAsync(CancellationToken token)
    {
        var fixture = new BackupCutFixture();
        try
        {
            using var store = new ZoneTreeStore(new(fixture.Source), UnitExecutionOptions.StorageExecution());
            fixture.Identity = store.Identity;
            store.Commit((tx, _) =>
            {
                tx.PutRecord(StoredKeyBytes, StoredValue);
                tx.PutRecord(StorageCommandKeys.Applied(null), new StoragePartitionPosition(null, InitialAuthorityValue));
                return true;
            });
            store.Commit((tx, _) =>
            {
                tx.PutRecord(StorageCommandKeys.Clock(null), InitialClock);
                return true;
            });
            fixture.Position = await store.CreateBackupAsync(fixture.Backup, token);
            return fixture;
        }
        catch (Exception)
        {
            fixture.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
