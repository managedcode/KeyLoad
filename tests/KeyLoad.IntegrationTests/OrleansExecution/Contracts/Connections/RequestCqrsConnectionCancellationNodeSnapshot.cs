namespace KeyLoad.IntegrationTests.OrleansExecution;

internal sealed record RequestCqrsConnectionCancellationNodeSnapshot(string Node, bool Overflowed,
    RequestCqrsConnectionCancellationRecord[] Records);
