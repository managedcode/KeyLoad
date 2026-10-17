namespace KeyLoad.UnitTests.Features.GraphTraversal;

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
