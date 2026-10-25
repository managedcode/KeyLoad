namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGateAnalyticsLinkProof(string Store, AnalyticsEventData Event,
    EntityRef OriginalReference, long OriginalGeneration);
