namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class PartitionIdentityProtocol
{
    internal const long NextCommit = 1;
    internal const int UnknownCapability = KeyLoad.Storage.StoreReaderContract.RuntimeJournal + 1;
    internal static byte[] OriginalKey => [0x71, 0x00, 0xF1];
    internal static byte[] OriginalValue => [0x81, 0x00, 0xF2];
    internal static byte[] HealthyKey => [0x73, 0x00, 0xF5];
    internal static byte[] HealthyValue => [0x83, 0x00, 0xF6];
}
