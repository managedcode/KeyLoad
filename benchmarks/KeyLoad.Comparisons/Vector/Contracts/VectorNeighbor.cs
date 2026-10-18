namespace KeyLoad.Comparisons;

/// <summary>A document returned by native nearest-neighbor search.</summary>
/// <param name="Id">The native document ID.</param>
/// <param name="Distance">The native cosine distance.</param>
public sealed record VectorNeighbor(string Id, double Distance);
