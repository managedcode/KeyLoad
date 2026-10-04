using KeyLoad.Storage.IO;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3OfflineFiles
{
    private const int BufferBytes = 65_536;

    internal static FileStream OpenRead(string path)
        => OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, BufferBytes);

    internal static void FlushToDisk(FileStream output)
        => output.Flush(flushToDisk: true);

    internal static void RequireRegular(string path)
        => OfflineRegularFile.RequireRegular(path);

    internal static void AssertExclusive(string path)
    {
        using var owner = OfflineRegularFile.Open(path, FileAccess.ReadWrite, FileShare.None, BufferBytes);
    }
}
