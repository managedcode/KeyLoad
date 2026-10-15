using KeyLoad.Storage.IO;

namespace KeyLoad.Replication;

/// <summary>Admits a retained snapshot input only from its original regular native entry.</summary>
internal static class ReplicaSnapshotReadOpen
{
    internal static FileStream Read(string path, int bufferBytes)
    {
        try
        {
            return OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, bufferBytes);
        }
        catch (KeyLoadException original) when (OfflineNativeErrors.IsInvalidEntry(original))
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot, original);
        }
    }
}
