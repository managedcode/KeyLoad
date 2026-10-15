namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record TransferredAtomicScopeState(CommandRequest Refused, OutboxStatus Outbox,
    DocumentResult? HealthyDocument = null);
