using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreFiles
{
    internal static FileStream OpenJournal(ZoneTreeStoreOptions options, FileMode mode = FileMode.OpenOrCreate) => new(
        Path.Combine(options.Directory, JournalFileName), mode,
        FileAccess.ReadWrite, FileShare.Read, FileBufferBytes, FileOptions.WriteThrough);

    internal static void CreatePrivateDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
