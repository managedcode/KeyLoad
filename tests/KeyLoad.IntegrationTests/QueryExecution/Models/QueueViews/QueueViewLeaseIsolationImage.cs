namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record QueueViewLeaseIsolationImage(MessageInspection[] Messages, StreamPage Source, OutboxStatus Outbox);
