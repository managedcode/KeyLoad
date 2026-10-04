namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeMetadataFile
{
    internal static ReadOnlyMemory<byte> Read(string path, int maximumBytes, string unsupportedDetail)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
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
