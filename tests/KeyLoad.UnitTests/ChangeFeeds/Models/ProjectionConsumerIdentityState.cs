namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionConsumerIdentityState(PartitionRef Partition, ProjectionConsumerRef Consumer,
    ProjectionConsumerDefinition Definition, ReplicatedOperation Configure, OperationResult Configured,
    ReplicatedOperation Seed, OperationResult Seeded, CommitReceipt SeedReceipt, DateTimeOffset Time,
    ProjectionBatch Batch, ReplicatedOperation Complete, ReplicatedOperation ConfigureAgain, byte[] Key, byte[] Original);
