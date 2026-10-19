using KeyLoad.Storage.ZoneTree.IO;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeOwnerLock
{
    internal static FileStream Open(ZoneTreeStoreOptions options, bool requireExisting)
    {
        var path = Path.Combine(options.Directory, OwnerLockFileName);
        try
        {
            return OfflineRegularFile.Open(path, FileAccess.ReadWrite, FileShare.None, options.StreamBufferBytes);
        }
        catch (FileNotFoundException) when (!requireExisting)
        {
            // Exclusive creation keeps this exact handle; a concurrent creator is not followed or retried.
            return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, options.StreamBufferBytes);
        }
    }
}
