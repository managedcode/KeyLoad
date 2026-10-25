namespace KeyLoad.ClusterTests.OrleansExecution;

internal sealed record RequestCqrsConnectionCancellationNodeSnapshot(string Node, bool Overflowed,
    RequestCqrsConnectionCancellationRecord[] Records);
