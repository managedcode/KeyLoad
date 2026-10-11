namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal enum GraphTraversalIntegrityFault
{
    MissingEdge,
    EdgeIdentity,
    EdgeSource,
    AdjacencyIdentity,
    InvalidLabel,
    InvalidTargetId,
    InvalidAttributes,
    NonpositiveRevision,
    NullTarget,
    MalformedAdjacency,
    WrongAdjacencyType
}

internal sealed record GraphTraversalOwnedFault(byte[] Key, byte[] Original);

internal sealed record GraphTraversalIntegrityState(CommandRequest SeedCommand, CommitReceipt SeedReceipt,
    EdgeRecord HealthyEdge, EdgeRecord DamagedEdge);

internal sealed record GraphTraversalIntegrityContinuation(CommandRequest Command, CommitReceipt Receipt, EdgeRecord Edge);
