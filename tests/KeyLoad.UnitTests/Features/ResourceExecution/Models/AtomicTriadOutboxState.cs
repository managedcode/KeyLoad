namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record AtomicTriadOutboxState(CommandRequest Command, ReplicatedOperation Failed,
    OperationResult Refusal, ReplicatedOperation Seed, OperationResult SeedResult, ReplicatedOperation Purge);

internal sealed record AtomicTriadOutboxContinuation(CommandRequest Command, ReplicatedOperation Healthy,
    OperationResult Result, OperationResult Purged, string[] Image, long Position);
