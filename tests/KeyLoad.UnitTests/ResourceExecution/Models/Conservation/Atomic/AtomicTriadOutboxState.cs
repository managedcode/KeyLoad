namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record AtomicTriadOutboxState(CommandRequest Command, ReplicatedOperation Failed,
    OperationResult Refusal, ReplicatedOperation Seed, OperationResult SeedResult, ReplicatedOperation Purge);
