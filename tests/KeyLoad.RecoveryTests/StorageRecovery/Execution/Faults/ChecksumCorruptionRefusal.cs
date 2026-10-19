using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal static class ChecksumCorruptionRefusal
{
    private const string RefusalSignalPrefix = "execution-checksum-corruption-refused";
    private const string ExpectedChecksumDetail = "The redo journal checksum is invalid.";
    private const string MissingRefusal = "The complete execution WAL frame did not fail closed with its checksum error.";
    internal static void Require(string root)
    {
        try
        {
            using var store = new ZoneTreeStore(new(root), CrashExecutionOptions.StorageExecution(),
                CrashExecutionOptions.PointCacheExecution());
        }
        catch (KeyLoadException failure) when (failure.Code == ErrorCode.Corruption && failure.Message == ExpectedChecksumDetail)
        {
            Console.WriteLine($"{RefusalSignalPrefix}{ChecksumProfileProtocol.SignalSeparator}{failure.Code}");
            return;
        }
        throw new InvalidOperationException(MissingRefusal);
    }
}
