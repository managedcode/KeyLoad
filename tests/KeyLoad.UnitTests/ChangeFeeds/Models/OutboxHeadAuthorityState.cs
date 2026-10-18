using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record OutboxHeadAuthorityState(PartitionRef Partition, Command<DatabaseCommand> Seed, CommitReceipt Receipt, Result<object> SeedResult,
    Command<DatabaseCommand> QuotaWrite, Result<object> QuotaFailure, Command<DatabaseCommand> Purge,
    byte[] Key, byte[] Original, long StoredBytes, long FirstBytes);
