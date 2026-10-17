namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionPairedIdentityProtocol
{
    internal const string JournalFile = "commands.wal";
    internal const long NextFrame = 1;
    internal const int MissingFinalByte = 1;
    internal const int UnknownCapability = KeyLoad.Storage.StoreReaderContract.RuntimeJournal + 1;
    internal static byte[] OriginalKey => [0x71, 0x00, 0xF1];
    internal static byte[] OriginalValue => [0x81, 0x00, 0xF2];
    internal static byte[] TornKey => [0x72, 0x00, 0xF3];
    internal static byte[] TornValue => [0x82, 0x00, 0xF4];
    internal static byte[] HealthyKey => [0x73, 0x00, 0xF5];
    internal static byte[] HealthyValue => [0x83, 0x00, 0xF6];
}
