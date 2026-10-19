namespace KeyLoad.Comparisons;

/// <summary>A document returned by execution nearest-neighbor search.</summary>
/// <param name="Id">The execution document ID.</param>
/// <param name="Distance">The execution cosine distance.</param>
public sealed record VectorNeighbor(string Id, double Distance);
