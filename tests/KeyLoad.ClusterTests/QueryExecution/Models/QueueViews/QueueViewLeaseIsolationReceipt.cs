namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record QueueViewLeaseIsolationReceipt(DeliveryCommand Command, CommitReceipt Receipt);
