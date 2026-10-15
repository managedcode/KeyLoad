namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record ConnectionQueryContextSeed(RequestCqrsPhaseFaultIdentity Identity, string AddedJson,
    CommandRequest Command, CommitReceipt Receipt, OutboxStatus Outbox);

internal sealed record ConnectionQueryContextState(ConnectionQueryContextSeed First, ConnectionQueryContextSeed Second);
