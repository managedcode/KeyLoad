namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record QueueViewLeaseIsolationImage(MessageInspection[] Messages, StreamPage Source, OutboxStatus Outbox);
