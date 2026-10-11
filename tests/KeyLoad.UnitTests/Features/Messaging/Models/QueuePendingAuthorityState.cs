namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueuePendingAuthorityFault
{
    MissingMarker,
    ForeignGeneration
}

internal enum QueuePendingAuthorityPhase
{
    Pending,
    Parked,
    Terminal
}

internal sealed record QueuePendingAuthorityState(QueueLifecycleTestState Flow, ReplicatedOperation Park,
    byte[] MarkerKey, byte[] MarkerBytes)
{
    internal List<QueuePendingAuthorityProcessed> Processed { get; } = [];
}

internal sealed record QueuePendingAuthorityProcessed(string Id, ProcessingRequest Request, CommitReceipt Receipt);

internal sealed record QueuePendingAuthorityCut(long Position, byte[] Identity, string[] Rows);
