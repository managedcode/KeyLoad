namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record AtomicTriadOutboxContinuation(CommandRequest Command, ReplicatedOperation Healthy,
    OperationResult Result, OperationResult Purged, string[] Image, long Position);
