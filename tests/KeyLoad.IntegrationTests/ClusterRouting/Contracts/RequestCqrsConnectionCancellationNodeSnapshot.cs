namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsConnectionCancellationNodeSnapshot(string Node, bool Overflowed,
    RequestCqrsConnectionCancellationRecord[] Records);
