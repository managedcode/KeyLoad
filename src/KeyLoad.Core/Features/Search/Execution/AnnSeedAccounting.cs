namespace KeyLoad.Core.Features.Search;

internal static class AnnSeedAccounting
{
    private const int HashScratchElementBytes = 1;
    private const int ArrayObjectAllowanceBytes = 64;
    private const int StringObjectAllowanceBytes = 64;
    private const long Utf16CodeUnitBytes = 2L;
    private const int StringTerminatorCharacters = 1;
    private const int AlignmentRemainderMask = 7;
    private const long AlignmentClearMask = ~7L;

    internal const string RecordLimit = "The ANN seed record limit is exceeded.";
    internal const string ByteLimit = "The ANN seed memory limit is exceeded.";
    private const long ControlAllowance = 2_048;
    private const long ScopeObjectAllowances = 64 + 64 + 64;
    private const long ResultObjectAllowance = 128;
    private const long DigestStringAllowance = 200;
    internal static long FixedBytes(int hashScratchBytes) => checked(ControlAllowance + ArrayAllowance(HashScratchElementBytes, hashScratchBytes)
        + ScopeObjectAllowances + ResultObjectAllowance + DigestStringAllowance);
    internal static long ArrayAllowance(int width, int length)
        => checked(ArrayObjectAllowanceBytes + Align8(checked((long)width * length)));

    internal static long StringAllowance(string value)
        => checked(StringObjectAllowanceBytes + Align8(checked(Utf16CodeUnitBytes * (value.Length + StringTerminatorCharacters))));

    internal static long Align8(long value) => checked((value + AlignmentRemainderMask) & AlignmentClearMask);
}
