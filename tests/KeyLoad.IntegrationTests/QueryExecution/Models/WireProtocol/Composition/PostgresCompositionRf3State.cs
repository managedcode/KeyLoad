namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresCompositionRf3State(PostgresCompositionRf3Seed Seed, CommandRequest Forward,
    CommitReceipt ForwardReceipt, CommandRequest Reverse, CommitReceipt ReverseReceipt, PostgresCompositionRf3Image Image);
