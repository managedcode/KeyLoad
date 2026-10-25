namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGateAnalyticsColdStore(FunctionalGateUserAnalyticsState State,
    CommandRequest Seed, CommitReceipt SeedReceipt, CommandRequest Update, CommitReceipt UpdateReceipt);
