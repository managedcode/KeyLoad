using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.IntegrationTests.StorageRecovery;

internal static class StorageOwnershipFiles
{
    private const int BufferBytes = 65_536;

    internal static FileStream OpenRead(string path)
        => OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, BufferBytes);

    internal static void AssertExclusive(string path)
    {
        using var owner = OfflineRegularFile.Open(path, FileAccess.ReadWrite, FileShare.None, BufferBytes);
    }
}
