using KeyLoad.Storage.IO;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Opens the original immutable archive journal without following or blocking on nonregular entries.</summary>
internal static class ZoneTreeBackupArchiveJournalOpen
{
    internal static FileStream Open(string path, int streamBufferBytes)
    {
        try
        {
            return OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, streamBufferBytes);
        }
        catch (KeyLoadException original) when (OfflineNativeErrors.IsInvalidEntry(original))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.BackupFileVerificationFailed, original);
        }
    }
}
