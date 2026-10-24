using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed record ProjectionConsumerIdentityState(TestDatabase Creation, PartitionRef Partition, ProjectionConsumerRef Consumer,
    ProjectionConsumerDefinition Definition, Command<DatabaseCommand> Configure, Result<object> Configured,
    Command<DatabaseCommand> Seed, Result<object> Seeded, CommitReceipt SeedReceipt, DateTimeOffset Time,
    ProjectionBatch Batch, Command<DatabaseCommand> Complete, Command<DatabaseCommand> ConfigureAgain, byte[] Key, byte[] Original);
