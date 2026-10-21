using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeBackupFileChecksum
{
    private const int NoRemainingBytes = 0;
    private const int FirstBufferByte = 0;
    private const int NoReadBytes = 0;
    private const int EndOfStream = -1;

    internal static void Verify(FileStream file, ZoneTreeBackupRestoreManifestFile item, byte[] buffer,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Compute(file, item.Length, buffer, cancellationToken), item.Checksum, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
        }
    }

    internal static string Compute(FileStream file, long length, byte[] buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (file.Length != length)
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var remaining = length;
        while (remaining > NoRemainingBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = file.Read(buffer.AsSpan(FirstBufferByte, (int)Math.Min(remaining, buffer.Length)));
            if (count == NoReadBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
            }
            hash.AppendData(buffer.AsSpan(FirstBufferByte, count));
            remaining -= count;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (file.ReadByte() != EndOfStream || file.Length != length)
        {
            throw Errors.Fail(ErrorCode.Corruption, BackupFileVerificationFailed);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
