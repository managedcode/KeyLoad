namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record QueueViewLeaseIsolationImage(MessageInspection[] Messages, StreamPage Source, OutboxStatus Outbox);
