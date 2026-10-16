namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record D2ArchiveHealthy(
    ReplicatedOperation Original,
    OperationResult Outcome,
    byte[] RetainedOutcome,
    OperationResult OldGenerationRefusal,
    long Position,
    string[] Image);
