namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed record AnalyticsCompositionCommit(ReplicatedOperation Operation, CommitReceipt Receipt, long CreatorEpoch);
