using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Opens the original immutable archive journal without following or blocking on nonregular entries.</summary>
internal static class ZoneTreeBackupArchiveJournalOpen
{
    private const int NoFileAttributes = 0;

    internal static FileStream Open(string path, int streamBufferBytes)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != NoFileAttributes)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.BackupFileIsLink);
        }
        try
        {
            return OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, streamBufferBytes);
        }
        catch (KeyLoadException original) when (OfflineErrors.IsInvalidEntry(original))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.BackupFileVerificationFailed, original);
        }
    }
}
