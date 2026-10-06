using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeSnapshotUpgradeIO
{
    private const int FileStartPosition = 0;
    private const int FirstBufferByte = 0;
    private const int EndOfStreamRead = 0;

    internal static T WithSource<T>(string path, long maximumBytes, int fileBufferBytes, Func<FileStream, T> operation)
    {
        using var lease = ZoneTreeSnapshotUpgradeFileLease.OpenSource(path, fileBufferBytes);
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

    internal static byte[] DigestFile(FileStream input, long maximumBytes, int fileBufferBytes)
    {
        RequireSnapshotSize(input, maximumBytes);
        input.Position = FileStartPosition;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[fileBufferBytes];
        int read;
        while ((read = input.Read(buffer, FirstBufferByte, buffer.Length)) != EndOfStreamRead)
        { digest.AppendData(buffer, FirstBufferByte, read); }

        input.Position = FileStartPosition;
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
