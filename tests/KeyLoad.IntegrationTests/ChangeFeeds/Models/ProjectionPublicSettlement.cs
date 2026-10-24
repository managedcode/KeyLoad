namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal sealed record ProjectionPublicSettlement(ProjectionPublicState Original, PrincipalRecord Current,
    CommitProjectionBatchRequest CurrentReplay, ProjectionBatchResult CurrentResult,
    ReleaseProjectionConsumerRequest Release, ProjectionConsumerInfo Released,
    PurgeOutboxRequest Purge, OutboxHead Purged);
