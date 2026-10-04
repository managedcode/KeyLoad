namespace KeyLoad.Core.Features.Search;

internal static class AnnSeedAccounting
{
    internal const string RecordLimit = "The ANN seed record limit is exceeded.";
    internal const string ByteLimit = "The ANN seed memory limit is exceeded.";
    internal const int HashScratchBytes = 4_096;
    private const long ControlAllowance = 2_048;
    private const long ScopeObjectAllowances = 64 + 64 + 64;
    private const long ResultObjectAllowance = 128;
    private const long DigestStringAllowance = 200;
    internal static long FixedBytes => checked(ControlAllowance + ArrayAllowance(1, HashScratchBytes)
        + ScopeObjectAllowances + ResultObjectAllowance + DigestStringAllowance);
    internal static long ArrayAllowance(int width, int length)
        => checked(64 + Align8(checked((long)width * length)));

    internal static long StringAllowance(string value)
        => checked(64 + Align8(checked(2L * (value.Length + 1))));

    internal static long Align8(long value) => checked((value + 7) & ~7L);
}
