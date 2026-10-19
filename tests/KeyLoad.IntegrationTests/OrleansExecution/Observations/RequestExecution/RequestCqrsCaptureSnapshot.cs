namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal sealed record RequestCqrsCaptureSnapshot(string Node, int Lines, int Candidates,
    int Accepted, int Malformed, int Oversized, bool Saturated,
    DateTimeOffset? FirstLineObservedAt, DateTimeOffset? LastLineObservedAt);
