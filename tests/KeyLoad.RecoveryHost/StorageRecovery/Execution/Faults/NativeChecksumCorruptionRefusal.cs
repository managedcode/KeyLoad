using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryHost;

internal static class NativeChecksumCorruptionRefusal
{
    private const string RefusalSignalPrefix = "native-checksum-corruption-refused";
    private const string ExpectedChecksumDetail = "The redo journal checksum is invalid.";
    private const string MissingRefusal = "The complete native WAL frame did not fail closed with its checksum error.";
    internal static void Require(string root)
    {
        try
        {
            using var store = new ZoneTreeStore(new(root), CrashExecutionOptions.StorageExecution(),
                CrashExecutionOptions.PointCacheExecution());
        }
        catch (KeyLoadException failure) when (failure.Code == ErrorCode.Corruption && failure.Message == ExpectedChecksumDetail)
        {
            Console.WriteLine($"{RefusalSignalPrefix}{NativeChecksumProfileProtocol.SignalSeparator}{failure.Code}");
            return;
        }
        throw new InvalidOperationException(MissingRefusal);
    }
}
