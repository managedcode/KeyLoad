using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeMetadataFile
{
    private static FileStream Open(string path, int streamBufferBytes, string unsupportedDetail)
    {
        try
        {
            return OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, streamBufferBytes);
        }
        catch (KeyLoadException original) when (OfflineErrors.IsInvalidEntry(original))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, unsupportedDetail, original);
        }
    }

    internal static ReadOnlyMemory<byte> Read(string path, int maximumBytes, int streamBufferBytes, string unsupportedDetail)
    {
        using var file = Open(path, streamBufferBytes, unsupportedDetail);
        if (file.Length > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, unsupportedDetail);
        }

        var bytes = new byte[maximumBytes + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = file.Read(bytes, count, bytes.Length - count);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        if (count > maximumBytes || file.Length > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, unsupportedDetail);
        }

        return new ReadOnlyMemory<byte>(bytes, 0, count);
    }
}
