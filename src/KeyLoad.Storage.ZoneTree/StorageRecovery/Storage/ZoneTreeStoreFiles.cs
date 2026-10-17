namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreFiles
{
    internal static FileStream OpenJournal(ZoneTreeStoreOptions options, FileMode mode = FileMode.OpenOrCreate)
        => ZoneTreeJournalFile.Open(options, mode);

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
