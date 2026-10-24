namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record QueueViewLeaseIsolationReceipt(DeliveryCommand Command, CommitReceipt Receipt);
