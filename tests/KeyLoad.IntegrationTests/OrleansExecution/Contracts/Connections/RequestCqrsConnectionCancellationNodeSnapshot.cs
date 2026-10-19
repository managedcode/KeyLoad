namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal sealed record RequestCqrsConnectionCancellationNodeSnapshot(string Node, bool Overflowed,
    RequestCqrsConnectionCancellationRecord[] Records);
