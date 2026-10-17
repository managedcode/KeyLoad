namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record QueueViewLeaseIsolationReceipt(DeliveryCommand Command, CommitReceipt Receipt);
