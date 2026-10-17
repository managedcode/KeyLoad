namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueScheduledAuthorityState(QueueLifecycleTestState Flow, DateTimeOffset Due,
    byte[] Key, byte[] Exact, ReplicatedOperation Receive)
{
    internal List<(ProcessingRequest Request, CommitReceipt Receipt)> Processed { get; } = [];
}
