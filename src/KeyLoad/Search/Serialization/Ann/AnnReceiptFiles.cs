using System.Security.Cryptography;
using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.Server.Features.Search;

internal static class AnnReceiptFiles
{
    private const int Empty = 0;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static byte[] Write<T>(string path, T value, AnnExecutionOptions options)
    {
        var bytes = OrleansSerialization.Serialize(value);
        if (bytes.Length <= Empty || bytes.Length > options.MaximumManifestBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProtocol.Bound);
        }
        FileStream? file = null;
        try
        {
            file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                options.FileBufferBytes, FileOptions.WriteThrough);
            file.Write(bytes);
            file.Flush(flushToDisk: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, PrivateFile);
            }
        }
        catch (Exception primary)
        {
            try
            {
                file?.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        file.Dispose();
        return SHA256.HashData(bytes);
    }

    internal static (T Value, byte[] Digest) Read<T>(string path, AnnExecutionOptions options)
    {
        FileStream? file = null;
        T value;
        byte[] digest;
        try
        {
            file = OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, options.FileBufferBytes);
            if (file.Length <= Empty || file.Length > options.MaximumManifestBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
            }
            var bytes = new byte[checked((int)file.Length)];
            file.ReadExactly(bytes);
            value = OrleansSerialization.Deserialize<T>(bytes);
            digest = SHA256.HashData(bytes);
        }
        catch (Exception primary)
        {
            try
            {
                file?.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        file.Dispose();
        return (value, digest);
    }
}
