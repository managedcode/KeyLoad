namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnLockFormat
{
    private const int Empty = 0;
    internal static void Require(FileStream file, NativeAnnExecutionOptions options)
    {
        if (file.Length <= Empty || file.Length > options.MaximumManifestBytes)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        if (NativeSerialization.Deserialize<int>(bytes) != NativeAnnProtocol.Version)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, NativeAnnProtocol.Ownership); }
    }
}
