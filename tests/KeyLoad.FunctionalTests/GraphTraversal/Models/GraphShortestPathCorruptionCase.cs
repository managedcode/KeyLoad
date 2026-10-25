namespace KeyLoad.FunctionalTests.GraphTraversal;

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
