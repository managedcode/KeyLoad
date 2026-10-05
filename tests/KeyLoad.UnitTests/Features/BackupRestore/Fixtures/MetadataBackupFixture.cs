using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class MetadataBackupFixture : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(),
        MetadataTestContract.DirectoryPrefix + Guid.NewGuid().ToString(MetadataTestContract.GuidFormat));

    internal string SourceDirectory => Path.Combine(root, MetadataTestContract.SourceDirectoryName);
    internal string BackupDirectory => Path.Combine(root, MetadataTestContract.BackupDirectoryName);
    internal string RestoredDirectory => Path.Combine(root, MetadataTestContract.RestoredDirectoryName);
    internal StoreIdentity OriginalIdentity { get; }

    internal MetadataBackupFixture()
    {
        try
        {
            using var store = new ZoneTreeStore(new(SourceDirectory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            OriginalIdentity = store.Identity;
            store.Commit((transaction, _) =>
            {
                transaction.PutRecord(KeyCodec.Encode(MetadataTestContract.StoredKey), MetadataTestContract.StoredValue);
                return true;
            });
            store.CreateBackup(BackupDirectory);
        }
        catch (Exception)
        {
            DeleteOwnedRoot();
            throw;
        }
    }

    internal static string ExpectedValue => MetadataTestContract.StoredValue;
    internal static byte[] StoredKeyBytes => KeyCodec.Encode(MetadataTestContract.StoredKey);

    public void Dispose()
    {
        DeleteOwnedRoot();
    }

    private void DeleteOwnedRoot()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
