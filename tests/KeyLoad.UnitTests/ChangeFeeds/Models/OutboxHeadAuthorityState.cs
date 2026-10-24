using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed record OutboxHeadAuthorityState(TestDatabase Creation, PartitionRef Partition, Command<DatabaseCommand> Seed, CommitReceipt Receipt, Result<object> SeedResult,
    Command<DatabaseCommand> Purge,
    byte[] Key, byte[] Original, long StoredBytes, long FirstBytes);
