namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed record ProjectionPartitionIsolationState(ProjectionConsumerIdentityState First,
    ProjectionConsumerIdentityState Second, ProjectionPartitionOutcome[] Outcomes, string[] Image, long Position,
    CommitReceipt? HealthySourceReceipt = null, CommitReceipt? HealthyEffectReceipt = null);
