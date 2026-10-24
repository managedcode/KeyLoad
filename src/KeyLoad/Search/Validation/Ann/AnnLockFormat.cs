namespace KeyLoad.Server.Search;

internal static class AnnLockFormat
{
    private const int Empty = 0;
    internal static void Require(FileStream file, AnnExecutionOptions options)
    {
        if (file.Length <= Empty || file.Length > options.MaximumManifestBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        if (OrleansSerialization.Deserialize<int>(bytes) != AnnProtocol.Version)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, AnnProtocol.Ownership);
        }
    }
}
