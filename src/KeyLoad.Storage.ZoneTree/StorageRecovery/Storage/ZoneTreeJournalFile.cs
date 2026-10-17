using KeyLoad.Storage.ZoneTree.IO;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeJournalFile
{
    internal static FileStream Open(ZoneTreeStoreOptions options, FileMode mode)
    {
        if (mode is not (FileMode.Open or FileMode.OpenOrCreate))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        var path = Path.Combine(options.Directory, JournalFileName);
        OfflineFileIdentity expected;
        try
        {
            expected = OfflineRegularFile.Inspect(path);
        }
        catch (FileNotFoundException) when (mode == FileMode.OpenOrCreate)
        {
            return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
                options.FileBufferBytes, FileOptions.WriteThrough);
        }
        return OfflineRegularFile.OpenWriteThrough(path, expected, FileShare.Read, options.FileBufferBytes);
    }
}
