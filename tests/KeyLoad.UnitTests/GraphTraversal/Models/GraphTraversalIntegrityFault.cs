namespace KeyLoad.UnitTests.GraphTraversal;

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
