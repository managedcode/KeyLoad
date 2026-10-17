namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionHostApplicationProtocol
{
    internal const int Callers = 4;
    internal const int FirstCaller = 0;
    internal const int FirstCold = 0;
    internal const long EmptyFileLength = 0;
    internal const int ColdCycles = 2;
    internal const int NoFailures = 0;
    internal const int OneFailure = 1;
    internal const int FailureIndex = 0;
    internal const long NextPosition = 1;
    internal const string OwnershipFile = "node.owner.lock";
    internal const string Separator = ":";
    internal const string LockWasNotExclusive = "The original native node owner did not exclude a second acquisition.";
    internal const string MissingFailure = "The real held node lock did not reject physical opening.";
    internal const string IncompleteCut = "The complete native host cut exceeded the configured record bound.";
    internal static byte[] FirstKey => [0xFE, 0x31, 0x00];
    internal static byte[] FirstValue => [0x80, 0x00, 0x7F];
    internal static byte[] SecondKey => [0xFE, 0x32, 0x00];
    internal static byte[] SecondValue => [0xFF, 0x02, 0x00, 0x81];
}
