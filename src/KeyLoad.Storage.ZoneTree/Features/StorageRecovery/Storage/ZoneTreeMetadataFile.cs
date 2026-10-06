namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeMetadataFile
{
    private const int OverBudgetProbeBytes = 1;
    private const int NoReadBytes = 0;
    private const int EndOfStreamRead = 0;
    private const int FirstBufferByte = 0;

    internal static ReadOnlyMemory<byte> Read(string path, int maximumBytes, int streamBufferBytes, string unsupportedDetail)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, streamBufferBytes);
        if (file.Length > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, unsupportedDetail);
        }

        var bytes = new byte[maximumBytes + OverBudgetProbeBytes];
        var count = NoReadBytes;
        while (count < bytes.Length)
        {
            var read = file.Read(bytes, count, bytes.Length - count);
            if (read == EndOfStreamRead)
            {
                break;
            }

            count += read;
        }

        if (count > maximumBytes || file.Length > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, unsupportedDetail);
        }

        return new ReadOnlyMemory<byte>(bytes, FirstBufferByte, count);
    }
}
