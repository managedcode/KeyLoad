using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeSnapshotUpgradeIO
{
    private const int BufferBytes = ZoneTreePersistenceFormat.FileBufferBytes;

    internal static T WithSource<T>(string path, long maximumBytes, Func<FileStream, T> operation)
    {
        using var lease = ZoneTreeSnapshotUpgradeFileLease.OpenSource(path);
        try
        {
            RequireSnapshotSize(lease.Stream, maximumBytes);
            return operation(lease.Stream);
        }
        catch (Exception error)
        {
            lease.RecordPrimary(error);
            throw;
        }
    }

    internal static byte[] DigestFile(FileStream input, long maximumBytes)
    {
        RequireSnapshotSize(input, maximumBytes);
        input.Position = 0;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferBytes];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
        { digest.AppendData(buffer, 0, read); }

        input.Position = 0;
        return digest.GetHashAndReset();
    }

    internal static void RequireSnapshotSize(FileStream input, long maximumBytes)
    {
        if (input.Length > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ZoneTreePersistenceFormat.SnapshotBytesExceeded); }
    }

    internal static void RequireEndOfFile(FileStream input)
    {
        if (input.Position != input.Length)
        { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.SnapshotTrailingData); }
    }
}
