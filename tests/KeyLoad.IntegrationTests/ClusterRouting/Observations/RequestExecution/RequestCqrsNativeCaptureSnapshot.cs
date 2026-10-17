namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsNativeCaptureSnapshot(string Node, int Lines, int Candidates,
    int Accepted, int Malformed, int Oversized, bool Saturated,
    DateTimeOffset? FirstLineObservedAt, DateTimeOffset? LastLineObservedAt);
