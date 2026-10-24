namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal sealed record FunctionalGateAnalyticsLinkProof(string Store, AnalyticsEventData Event,
    EntityRef OriginalReference, long OriginalGeneration);
