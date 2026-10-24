namespace KeyLoad.UnitTests.GraphTraversal;

internal enum GraphShortestPathCorruptionCase
{
    MissingEdge,
    EdgeIdentity,
    EdgeSource,
    ForeignTarget,
    InvalidLabel,
    InvalidTargetId,
    MalformedAdjacency,
    WrongAdjacencyType
}
