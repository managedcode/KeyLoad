namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateAnalyticsLinkProof(string Store, AnalyticsEventData Event,
    EntityRef OriginalReference, long OriginalGeneration);
