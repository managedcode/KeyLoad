using KeyLoad.Storage.IO;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OfflineRegularFileLockTests
{
    private const string RootPrefix = "keyload-offline-file-lock-";

    [Test]
    public async Task AcEpoch012InteropWithBclFileShareLocksInBothDirections()
    {
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "locked.bin");
        await File.WriteAllBytesAsync(path, [0x31, 0x42]);
        try
        {
            using (OfflineRegularFile.Open(path, FileAccess.Read, FileShare.None, 4096))
            {
                Assert.ThrowsExactly<IOException>(() =>
                {
                    using var unexpected = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _ = unexpected.ReadByte();
                });
            }

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsExactly<IOException>(() =>
                {
                    using var unexpected = OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, 4096);
                    _ = unexpected.ReadByte();
                });
            }

            using var reopened = OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, 4096);
            await Assert.That(reopened.ReadByte()).IsEqualTo(0x31);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
