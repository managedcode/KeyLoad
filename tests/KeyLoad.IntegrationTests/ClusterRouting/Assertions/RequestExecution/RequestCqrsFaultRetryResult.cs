namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsFaultRetryResult(CommitReceipt SdkReceipt, CommitReceipt McpReceipt, Guid McpRequestId);
