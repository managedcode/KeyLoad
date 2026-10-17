namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed record AnalyticsStoreTestCommit(ReplicatedOperation Operation, CommitReceipt Receipt);
